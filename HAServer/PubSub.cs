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

// Client access permissions to a channel are applied at subscribe time. The channel 'auth' property has a list of groups & access for that group (R, RW). 
// THere is a separate user group table 'clientGroups' that has the clientname and the list of groups the client is allocated to. 
// When a client subscribes, the client is checked to see if the relevant group they are a member of are in the channel group list. 
// If so, the client is granted the access rights of the matching group they are a member of from 'auth' property, which is then written into the channel 'clients' property list including their access rights (for fast per message lookup).

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

        private static Consts.ServiceState _serviceState = Consts.ServiceState.PAUSED;                  // Accept messages but wait for all the services to start before processing

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
                Task.Factory.StartNew(() =>                                                                 // Manage messages on the queue via separate thread
                {
                    var channelKey = new ChannelKey();
                    foreach (Commons.HAMessage HAMessage in messQ.GetConsumingEnumerable())
                    {
                        while (_serviceState != Consts.ServiceState.RUNNING) Thread.Sleep(10);              // If the service has stopped or paused, block don't process the message queue

                        channelKey.network = HAMessage.network;
                        channelKey.category = HAMessage.category;
                        channelKey.className = HAMessage.className;
                        channelKey.instance = HAMessage.instance;
                        if (channels.ContainsKey(channelKey))
                        {
                            var clients = channels[channelKey].clients;                                         // Get subscribing clients
                            foreach (var client in clients.ToList())                                        // ToList avoids contention when looping while the original collection is modified
                            {
                                if (client.access.Contains("R"))                            // Read allows us to subscribe & receive messages
                                {
                                    var clientRoute = client.name.Split('.');
                                    switch (clientRoute[0].ToUpper())
                                    {
                                        case "EXTENSIONS":
                                            Core.extensions.RouteMessage(clientRoute[1], HAMessage, channels[channelKey].source);
                                            break;
                                        case "PLUGINS":
                                            //Core.plugins.RouteMessage(client.name, HAMessage);
                                            break;
                                        case "CORE":
                                            //Core.RouteMessage(client.name, HAMessage);
                                            break;
                                        default:
                                            Logger.LogError("Can't route message " + HAMessage.instance + "\\" + HAMessage.instance + "\\" + HAMessage.instance + " to " + client.name);
                                            break;
                                    }
                                }
                            }
                        }
                        if (Core.DebugMode) Logger.LogDebug(String.Format("Category: {0}, Class: {1}, Instance: {2}, Scope: {3}, Data: {4}", HAMessage.category, HAMessage.className, HAMessage.instance, HAMessage.scope, HAMessage.data));
                        // TODO: Log to timeseries

                    }
                }, TaskCreationOptions.LongRunning);

                // Setup access groups
                clientGroups["EXTENSIONS"] = new List<string>();
                //-------
                // Test
                clientGroups["ADMINS"] = new List<string> { "PubSub.myClient", "Extensions.Rules" };

                AddUpdChannel("Rules", new ChannelKey
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
                }, "Extensions");           // Test route to extensions

                AddUpdChannel("Rules", new ChannelKey
                {
                    network = "Another Network",
                    category = "LIGHTING",
                    className = "CBUS",
                    instance = "HALLWAY"
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
                }, "Extensions");

                _serviceState = Consts.ServiceState.RUNNING;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        // Submit a message to the event message queue. Requires the message structure to be prepopulated
        //TODO: Is clientname needed? No, as routing will occur based on subscription info - publish only puts on message queue. subscription will check access rights. 
        // Only allow registered clients with a subscription can publish so that errant plugins/inputs cant send messages when they don't have rights. Use GUID to validate access in the net protocol handler
        //TODO: caller won't be populated by plugins. Also publish for plugins should be restricted to cat/classname. Need to do this via reflection, but how expensive?
        //TODO: Check that caller has subscribed to this channel before allowing to publish. Use a guid when subscribing that is passed back and then used for all publish? MQTT supports this??
        //TODO: Return value if not successful. Maybe move message log write to another queue & thread
        public async void Publish(ChannelKey channel, string scope, string data)
        {
            try
            {
                if (_serviceState != Consts.ServiceState.STOPPED && messQ.Count < 1000)                      // Don't add messages to the message queue if service is stopped, and if it is paused for too long start dropping messages
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

                    //TODO: Check if caller has access rights

                    if (messQ.TryAdd(myMessage))
                    {
                        await Core.timeSeries.WriteTS(myMessage);                                               // Log to message log
                    };
                    //return true;
                }
                //return false;                                                                                   // Can't add messae
            }
            catch (Exception ex)
            {
                //TODO
                Logger.LogCritical("Can't submit message to message queue - Exiting. Error: " + Environment.NewLine + ex.ToString());
                throw;
            }
        }

        // Create channels.
        //TODO: WHat happens if the same channel is created several times.
        //TODO" Need security on adding channels
        public bool AddUpdChannel(string clientName, ChannelKey channel, ChannelSub channelSub, [CallerMemberName] string caller = "")
        {
            channelSub.author = Path.GetFileNameWithoutExtension(caller);                                                             // Enforce author as caller, regardless of setting passed.
            //var subKeys = channels.Keys;                                                                   // Get the existing channel keys before adding the new one
            channels[channel] = channelSub;                                                    // Create new channel

            // Add any previous requests to subscribe to this channel before it was created
            foreach (var oldSub in subRequests)                                             // Does the old subscription request match the new channel including wildcards?
            {
                if (oldSub.channelKey.network == "" || oldSub.channelKey.network == channel.network)
                {
                    if (oldSub.channelKey.category == "" || oldSub.channelKey.category == channel.category)
                    {
                        if (oldSub.channelKey.className == "" || oldSub.channelKey.className == channel.className)
                        {
                            if (oldSub.channelKey.instance == "" || oldSub.channelKey.instance == channel.instance)
                            {
                                Subscribe(oldSub.clientName, oldSub.channelKey, oldSub.caller);
                            }
                        }
                    }
                }
            }

            Subscribe(clientName, channel, channelSub.author);                                             // Caller auto subscribe
            // TODO: true is redundant
            return true;
        }

        // Subscribe to channel for Ext/Plug.Client. Return null if channel does not exist else return the number of channels the request was granted a subscription
        public int Subscribe(string clientName, ChannelKey channel, [CallerFilePath] string caller = "")
        {
            string clientAccess;
            var fullClientName = Path.GetFileNameWithoutExtension(caller) + "." + clientName;
            //if (!channels.Keys.Contains(channel)) subRequests.Add(new SubRequests { clientName = clientName, channelKey = channel, caller = caller });  // Save subscription request to add to any future channels added if the channel isn't established now
            subRequests.Add(new SubRequests { clientName = clientName, channelKey = channel, caller = caller });  // Save subscription request to add to any future channels added including wildcards

            var subKeys = channels.Keys;                                                                   // Filter keys so that wildcard "" (ALL) is catered for.
            if (channel.network != "") subKeys = subKeys.Where(x => x.network == channel.network).ToList();
            if (channel.category != "") subKeys = subKeys.Where(x => x.category == channel.category).ToList();
            if (channel.className != "") subKeys = subKeys.Where(x => x.className == channel.className).ToList();
            if (channel.instance != "") subKeys = subKeys.Where(x => x.instance == channel.instance).ToList();

            // Check the auth table for group access and if the subscribing entity is part of a group in the auth table.
            foreach (var subKey in subKeys)                                                              // Loop through all channels that match subscription request
            {
                clientAccess = "";
                var subscription = channels[subKey];
                foreach (var subGroup in subscription.auth)                                         // Get access rights for group client is a member of
                {
                    if (clientGroups.TryGetValue(subGroup.name, out var clientGroup))                                   // Lookup clients associated with group
                    {
                        if (clientGroup.Contains(fullClientName))
                        {
                            clientAccess = subGroup.access;
                            if (subGroup.access == "RW") break;                                         // RW access takes precidence if user is a member of multiple groups
                        }
                    }
                }

                // Setup specific access for the subscribing entity from the group lookup into the subscription clients table for faster message processing
                if (clientAccess != "")                                                                   // Some access allowed so setup
                {
                    var newAccess = new AccessAttribs
                    {
                        name = fullClientName,
                        access = clientAccess
                    };
                    var exists = subscription.clients.FindIndex(x => x.name == fullClientName);     // Look for fullClientName already in list (assume there could be any access strings)
                    if (exists != -1)
                    {
                        subscription.clients[exists] = newAccess;                                   // Update if existing
                    }
                    else
                    {
                        subscription.clients.Add(newAccess);                                        // Add if new
                    }
                    channels[channel] = subscription;                                          // Update subscription with new client access info
                }
            }
            return subKeys.Count;
        }

        // Get values from extension ini file
        public string GetIniSection(string section, [CallerFilePath] string caller = "")
        {
            var extName = Path.GetFileNameWithoutExtension(caller);
            if (caller == "" || !Core.extensions.extInis.Keys.Contains(extName)) return null;        // Check if asking for the right extension
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