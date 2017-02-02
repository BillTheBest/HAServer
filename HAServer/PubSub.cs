using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Interfaces;
using System;

//clients are <plugin/extension.clientname>
//TODO: Persist user group table to file and secure it, modify access via admin UI

namespace HAServer
{
/*    public interface IExtension
    {
        string ExtStart(IPubSub myHost);
        string ExtStop(string param);
    }

    public interface IPubSub
    {
        bool AddUpdChannel(ChannelKey channel, ChannelSub channelSub, [CallerMemberName] string caller = "");
        string Subscribe(string clientName, ChannelKey channel, [CallerFilePath] string caller = "");
        bool Publish(string clientName, ChannelKey channel, string message, [CallerFilePath] string caller = "");
    }

    public struct ChannelKey
    {
        public string network;
        public string category;
        public string className;
        public string instance;
    }

    public struct AccessAttribs
    {
        public string name;
        public string access;
    }

    public class ChannelSub
    {
        public bool active = true;
        public string desc = "";
        public string type = "GENERIC";
        public string author = "";
        public List<AccessAttribs> clients = new List<AccessAttribs>();                                         // List of clients subscribed & their access rights. This is set when subscribing to enable fast lookup when processing messages
        public List<AccessAttribs> auth = new List<AccessAttribs>();                                            // When subscribing, clients must be a member of one of these groups & will get the access rights of that group
        public List<KeyValuePair<string, string>> attribs = new List<KeyValuePair<string, string>>();
    } */

    public class PubSub : IPubSub
    {
        static ILogger Logger { get; } = ApplicationLogging.CreateLogger<PubSub>();

        // Subscription table
        private static ConcurrentDictionary<ChannelKey, ChannelSub> subscriptions = new ConcurrentDictionary<ChannelKey, ChannelSub>();

        // User group table
        private static ConcurrentDictionary<string, List<string>> clientGroups = new ConcurrentDictionary<string, List<string>>();

        public PubSub()
        {
            try
            {
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

        public bool Publish(string clientName, ChannelKey channel, string message, [CallerFilePath] string caller = "")
        {
            Logger.LogInformation("Client: " + clientName + " published to: " + channel.instance + " TEST");
            return true;
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
    }
}

