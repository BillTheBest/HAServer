using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

//clients are <plugin/extension.clientname>
//TODO: Persist user group table to file and secure it, modify access via admin UI
//TODO: Put /// comments on all public external methods

namespace HAServer
{
    public class PubSub : IPubSub
    {
        static ILogger Logger = ApplicationLogging.CreateLogger<PubSub>();

        // Subscription table
        private static ConcurrentDictionary<ChannelKey, ChannelSub> subscriptions = new ConcurrentDictionary<ChannelKey, ChannelSub>();

        // User group table
        private static ConcurrentDictionary<string, List<string>> clientGroups = new ConcurrentDictionary<string, List<string>>();

        // Main message queue
        private static BlockingCollection<Commons.HAMessage> messQ = new BlockingCollection<Commons.HAMessage>();

        private static Consts.ServiceState _serviceState = Consts.ServiceState.STOPPED;

        public PubSub()
        {
            try
            {
                Task.Factory.StartNew(() =>                                                                 // Manage messages on the queue
                {
                    foreach (Commons.HAMessage HAMessage in messQ.GetConsumingEnumerable())
                    {
                        while (_serviceState != Consts.ServiceState.RUNNING) Thread.Sleep(10);              // If the service has stopped or paused, block don't process the message queue

                        Task.Factory.StartNew(() => HandleMessage(HAMessage));                              // Start message consumer tasks

                        if (Core.DebugMode) Logger.LogDebug(String.Format("Category: {0}, Class: {1}, Instance: {2}, Scope: {3}, Data: {4}", HAMessage.category, HAMessage.className, HAMessage.instance, HAMessage.scope, HAMessage.data));
                    }
                }, TaskCreationOptions.LongRunning);

                _serviceState = Consts.ServiceState.PAUSED;                                                 // Accept messages but wait for all the services to start before processing

                // Test
                List<string> users = new List<string>();
                clientGroups["ADMINS"] = new List<string> { "PubSub.myClient" };
                AddUpdChannel(new ChannelKey
                {
                    network = Core.networkName,
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
                    }
                },
                    clients = new List<AccessAttribs>
                {
                    new AccessAttribs
                    {
                        name = "PubSub.myClient",
                        access = "RW"
                    }
                }
                });

                Subscribe("myClient", new ChannelKey
                {
                    network = Core.networkName,
                    category = "LIGHTING",
                    className = "CBUS",
                    instance = "MASTERCOCOON"
                });
            }
            catch (Exception ex)
            {

                throw ex;
            }
        }

        // THREAD: Handle messages from the message queue
        private void HandleMessage(Commons.HAMessage myMessage)
        {
            //TODO: If channel info is incomplete then send all children channel (eg. className = CBUS, instance = "" will send all lights messages)
            Logger.LogInformation("Hnadling new message");
        }

        // UNUSED
        public bool HostFunc(string func, string cat, string className, string instance, string scope, string data)
        {
            var myMessage = new Commons.HAMessage
            {
                network = Core.networkName,
                category = cat,
                className = className,
                instance = instance,
                scope = scope,
                data = data
            };
            return true;
        }

        // Submit a message to the event message queue. Requires the message structure to be prepopulated
        //TODO: Is clientname needed? caller won't be populated by plugins
        public async void Publish(string clientName, ChannelKey channel, string scope, string data, [CallerMemberName] string caller = "")
        {
            try
            {
                if (_serviceState != Consts.ServiceState.STOPPED && messQ.Count < 1000)                      // Don't add messages to the message queue if service is stopped, and if it is paused for too long start dropping messages
                {
                    var myMessage = new Commons.HAMessage
                    {
                        network = Core.networkName,
                        category = channel.category,
                        className = channel.className,
                        instance = channel.instance,
                        scope = scope,
                        data = data
                    };

                    if (messQ.TryAdd(myMessage))
                    {
                        await Core.timeSeries.WriteTS(myMessage);                                               // Log to message log
                    };
                }
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
            subscriptions[channel] = channelSub;
            return true;
        }

        // Subscribe to channel for Ext/Plug.Client. Return null if channel does not exist else return access rights ("", R, RW) and update any old entries or add new
        public string Subscribe(string clientName, ChannelKey channel, [CallerFilePath] string caller = "")
        {
            string access = "";
            var fullClientName = Path.GetFileNameWithoutExtension(caller) + "." + clientName;
            if (subscriptions.TryGetValue(channel, out var subscription))                           // Get channel subscription info
            {
                foreach (var subGroup in subscription.auth)                                         // Get access rights for group client is a member of
                {
                    clientGroups.TryGetValue(subGroup.name, out var clientGroup);                   // Lookup clients associated with group
                    if (clientGroup.Contains(fullClientName))
                    {
                        access = subGroup.access;
                        if (subGroup.access == "RW") break;                                         // RW access takes precidence if user is a member of multiple groups
                    }
                }

                if (access != "")                                                                   // Some access allowed so setup
                {
                    var newAccess = new AccessAttribs
                    {
                        name = fullClientName,
                        access = access
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
                    subscriptions[channel] = subscription;                                          // Update subscription with new client access info
                }

                return access;
            }
            return null;                                                                            // subscription does not exist
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

        // Any shutdown code
        public void Shutdown()
        {
        }

    }
}

