using Commons;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// Definitions to be shared across main application, extensions and plugins
//TODO: Consider using simpler interfaces and inheriting these to make more complex ones
namespace Interfaces
{
    public interface IExtension
    {
        string Start();
        string Stop();
    }

    public interface IPlugin
    {
        string PlugStart(IPubSub myHost);
        string PlugStop(string param);
    }

    public interface IPubSub
    {
        bool AddUpdChannel(ChannelKey channel, ChannelSub channelSub, [CallerMemberName] string caller = "");

        /// <summary> 
        /// Subscribe to a specific channel. 
        /// 
        /// A subscription will send any messages in the channel to the instance of the subscriber. 
        /// </summary> 
        /// <param name="clientName">The name of the requesting client</param> 
        /// <param name="channel">The channel to subscribe to (and any children)</param>
        int Subscribe(string clientName, ChannelKey channel, [CallerFilePath] string caller = "");

        void Publish(string clientName, ChannelKey channel, string scope, string data, [CallerFilePath] string caller = "");

        string GetIniSection(string section, [CallerFilePath] string caller = "");

        bool WriteLog(Commons.LOGTYPES type, string desc, [CallerFilePath] string caller = "");
    }

    //////////// Classes / Structs

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
        public string source = "";
        public string desc = "";
        public string type = "";
        public string io = "";
        public string min = "0";
        public string max = "100";
        public string units = "";
        public string author = "";
        public List<AccessAttribs> clients = new List<AccessAttribs>();                                         // List of clients subscribed & their access rights. This is set when subscribing to enable fast lookup when processing messages
        public List<AccessAttribs> auth = new List<AccessAttribs>();                                            // When subscribing, clients must be a member of one of these groups & will get the access rights of that group
        public List<KeyValuePair<string, string>> attribs = new List<KeyValuePair<string, string>>();
    }
}

// TODO: Get this compiled as dotnet standard 1.6 which is compatible with .net core.
namespace Commons
{
    public class Globals
    {
        public static string networkName;
        public static List<CatStruc> categories = new List<CatStruc>();

        public Globals()
        {
        }
    }

    // Associate icon with category
    public struct CatStruc
    {
        public string name;
        public string icon;
    }

    public class HAMessage
    {
        public string network;
        public string category;
        public string className;
        public string instance;
        public string scope;
        public string data;
    }

    public enum LOGTYPES
    {
        INFORMATION,
        WARNING,
        ERROR
    }
}
