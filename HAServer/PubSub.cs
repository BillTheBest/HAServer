using Commons;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

//clients are <plugin/extension.clientname>
//TODO: Persist user group table to file and secure it, modify access via admin UI
//TODO: Put /// comments on all public external methods

// These classes handle the message processing based on publish / subscribe channel setup
namespace HAServer
{
    public class PubSub : IPubSub
    {
        static ILogger Logger = ApplicationLogging.CreateLogger<PubSub>();

        // Subscription table
        private static ConcurrentDictionary<ChannelKey, ChannelSub> channels = new ConcurrentDictionary<ChannelKey, ChannelSub>();

        // User group table
        private static ConcurrentDictionary<string, List<string>> clientGroups = new ConcurrentDictionary<string, List<string>>();

        // Main message queue
        private static BlockingCollection<Commons.HAMessage> messQ = new BlockingCollection<Commons.HAMessage>();

        private static Consts.ServiceState _serviceState = Consts.ServiceState.STOPPED;

        // Save old subscription requests
        public struct SubRequests
        {
            public string clientName;
            public ChannelKey channelKey;
            public string caller;
        }

        private static BlockingCollection<SubRequests> subRequests = new BlockingCollection<SubRequests>();

        public PubSub()
        {
            try
            {
                Task.Factory.StartNew(() =>                                                                 // Manage messages on the queue via separate thread
                {
                    var channelKey = new ChannelKey();
                    foreach (Commons.HAMessage HAMessage in messQ.GetConsumingEnumerable())
                    {
                        while (_serviceState != Consts.ServiceState.RUNNING) Thread.Sleep(10);              // If the service has stopped or paused, block don't process the message queue

                        // Log to timeseries

                        channelKey.network = HAMessage.network;
                        channelKey.category = HAMessage.category;
                        channelKey.className = HAMessage.className;
                        channelKey.instance = HAMessage.instance;
                        if (channels.ContainsKey(channelKey))
                        {
                            var clients = channels[channelKey].clients;                                         // Get subscribing clients
                            foreach (var client in clients)
                            {
                                if (client.access.Contains("R"))                            // Read allows us to subscribe & receive messages
                                {
                                    //Core.RouteMessage(client.name, HAMessage);
                                    Core.extensions.RouteMessage(client.name, HAMessage);
                                    //Core.plugins.RouteMessage(client.name, HAMessage);
                                }
                            }
                        }
                        if (Core.DebugMode) Logger.LogDebug(String.Format("Category: {0}, Class: {1}, Instance: {2}, Scope: {3}, Data: {4}", HAMessage.category, HAMessage.className, HAMessage.instance, HAMessage.scope, HAMessage.data));
                    }
                }, TaskCreationOptions.LongRunning);

                _serviceState = Consts.ServiceState.PAUSED;                                                 // Accept messages but wait for all the services to start before processing

                // Setup access groups
                clientGroups["EXTENSIONS"] = new List<string>();


                // Test
                clientGroups["ADMINS"] = new List<string> { "PubSub.myClient" };

                AddUpdChannel(new ChannelKey
                {
                    network = Globals.networkName,
                    category = "LIGHTING",
                    className = "CBUS",
                    instance = "MASTERCOCOON"
                }, new ChannelSub
                {
                    auth = new List<AccessAttribs>
                {
                    new AccessAttribs
                    {
                        name = "ADMINS",
                        access = "RW"
                    },
                    new AccessAttribs
                    {
                        name = "EXTENSIONS",
                        access = "RW"
                    }
                }
                });

                AddUpdChannel(new ChannelKey
                {
                    network = "XX",
                    category = "LIGHTING",
                    className = "CBUS",
                    instance = "MASTERCOCOON"
                }, new ChannelSub
                {
                    auth = new List<AccessAttribs>
                {
                    new AccessAttribs
                    {
                        name = "ADMINS",
                        access = "RW"
                    },
                    new AccessAttribs
                    {
                        name = "EXTENSIONS",
                        access = "RW"
                    }
                }
                });
            }
            catch (Exception ex)
            {

                throw ex;
            }
        }

        // Submit a message to the event message queue. Requires the message structure to be prepopulated
        //TODO: Is clientname needed? caller won't be populated by plugins. Also publish for plugins should be restricted to cat/classname. Need to do this via reflection, but how expensive?
        //TODO: Return value if not successful. Maybe move message log write to another queue & thread
        public async void Publish(string clientName, ChannelKey channel, string scope, string data, [CallerFilePath] string caller = "")
        {
            try
            {
                if (_serviceState != Consts.ServiceState.STOPPED && messQ.Count < 1000)                      // Don't add messages to the message queue if service is stopped, and if it is paused for too long start dropping messages
                {
                    var myMessage = new Commons.HAMessage
                    {
                        network = Globals.networkName,
                        category = channel.category,
                        className = channel.className,
                        instance = channel.instance,
                        scope = scope,
                        data = data
                    };

                    //TODO: Check that caller has W access

                    if (messQ.TryAdd(myMessage))
                    {
                        await Core.timeSeries.WriteTS(myMessage);                                               // Log to message log
                    };
                    //return true;
                }
                //return false;                                                                                   // Can't add messae
            }
            catch (Exception ex)
            {
                //TODO
                Logger.LogCritical("Can't submit message to message queue - Exiting. Error: "  + Environment.NewLine + ex.ToString());
                throw;
            }
        }

        // Channels must be created before subscribed to.
        public bool AddUpdChannel(ChannelKey channel, ChannelSub channelSub, [CallerMemberName] string caller = "")
        {
            channelSub.author = caller;                                                             // Enforce author as caller, regardless of setting passed.
            channels[channel] = channelSub;                                                    // Create new channel

            // Add any previous requests to subscribe to this channel before it was created
            foreach(var oldSub in subRequests)
            {
                var subKeys = channels.Keys;                                                                   // Filter keys so that wildcard "" (ALL) is catered for.
                if (oldSub.channelKey.network != "") subKeys = subKeys.Where(x => x.network == oldSub.channelKey.network).ToList();
                if (oldSub.channelKey.category != "") subKeys = subKeys.Where(x => x.category == oldSub.channelKey.category).ToList();
                if (oldSub.channelKey.className != "") subKeys = subKeys.Where(x => x.className == oldSub.channelKey.className).ToList();
                if (oldSub.channelKey.instance != "") subKeys = subKeys.Where(x => x.instance == oldSub.channelKey.instance).ToList();
                if (subKeys.Count > 0) Subscribe(oldSub.clientName, oldSub.channelKey, oldSub.caller);
            }

            return true;
        }

        // Subscribe to channel for Ext/Plug.Client. Return null if channel does not exist else return access rights ("", R, RW) and update any old entries or add new
        public int Subscribe(string clientName, ChannelKey channel, [CallerFilePath] string caller = "")
        {
            string clientAccess;
            var fullClientName = Path.GetFileNameWithoutExtension(caller) + "." + clientName;
            subRequests.Add(new SubRequests { clientName = clientName, channelKey = channel, caller = caller });        // Save subscription request to add to any future channels added

            var subKeys = channels.Keys;                                                                   // Filter keys so that wildcard "" (ALL) is catered for.
            if (channel.network != "") subKeys = subKeys.Where(x => x.network == channel.network).ToList();
            if (channel.category != "") subKeys = subKeys.Where(x => x.category == channel.category).ToList();
            if (channel.className != "") subKeys = subKeys.Where(x => x.className == channel.className).ToList();
            if (channel.instance != "") subKeys = subKeys.Where(x => x.instance == channel.instance).ToList();

            // Check the auth table for group access and if the subscribing entity is part of a group in the auth table.
            foreach (var subKey in subKeys)                                                              // Loop through all channels that match subscription request
            {
                clientAccess = "";
                var subscription = channels[subKey];
                foreach (var subGroup in subscription.auth)                                         // Get access rights for group client is a member of
                {
                    if (clientGroups.TryGetValue(subGroup.name, out var clientGroup))                                   // Lookup clients associated with group
                    {
                        if (clientGroup.Contains(fullClientName))
                        {
                            clientAccess = subGroup.access;
                            if (subGroup.access == "RW") break;                                         // RW access takes precidence if user is a member of multiple groups
                        }
                    }
                }

                // Setup specific access for the subscribing entity from the group lookup into the subscription clients table for faster message processing
                if (clientAccess != "")                                                                   // Some access allowed so setup
                {
                    var newAccess = new AccessAttribs
                    {
                        name = fullClientName,
                        access = clientAccess
                    };
                    var exists = subscription.clients.FindIndex(x => x.name == fullClientName);     // Look for fullClientName already in list (assume there could be any access strings)
                    if (exists != -1)
                    {
                        subscription.clients[exists] = newAccess;                                   // Update if existing
                    }
                    else
                    {
                        subscription.clients.Add(newAccess);                                        // Add if new
                    }
                    channels[channel] = subscription;                                          // Update subscription with new client access info
                }
            }
            return subKeys.Count;
        }

        // Get values from extension ini file
        public string GetIniSection(string section, [CallerFilePath] string caller = "")
        {
            var extName = Path.GetFileNameWithoutExtension(caller);
            if (caller == "" || !Core.extensions.extInis.Keys.Contains(extName)) return null;        // Check if asking for the right extension
            return Core.extensions.extInis[extName].GetSection(section).Value;
        }

        public bool WriteLog(Commons.LOGTYPES type, string desc, [CallerFilePath] string caller = "")
        {
            Logger.LogInformation("Message from Extension or Plugin " + Path.GetFileNameWithoutExtension(caller).ToUpper() + " - " + desc);
            return true;
        }

        // Control the actions of the main message queue
        public void SetServerState(Consts.ServiceState setState)
        {
            _serviceState = setState;
        }

        // Show current state of the message queue
        public Consts.ServiceState GetServerState()
        {
            return _serviceState;
        }

        //Add a user to an access group
        public bool AddUserToAccessGroup(string group, string user)
        {
            clientGroups[group].Add(user);
            return true;
        }

        // Any shutdown code
        public void Shutdown()
        {
        }

    }
}

