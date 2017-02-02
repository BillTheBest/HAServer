using System.Collections.Generic;
using System.Runtime.CompilerServices;

//TODO: Remove NetStandard.library from NuGet as it is redundant (it is automatically included in 1.1)
namespace Interfaces
{
    public interface IExtension
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
    }
}

// Get this compiled as dotnet standard 1.6 which is compatible with .net core.
namespace Commons
{
}
