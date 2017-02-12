using System;
using Interfaces;
using Commons;

namespace TestPlugin
{
    public class MyPlugin
    {
        private IPubSub _host;

        public MyPlugin(IPubSub pubSub)
        {
            _host = pubSub;
        }

        // Runs at startup
        public string Startup(string startParam)
        {
            try
            {
                var yy = 0;
                //var tt = 1 / yy;
                _host.Publish("MyPlugin", new ChannelKey()
                {        // TODO: Maybe use callername instead of hard coding plugin name
                        category = "dd",
                        className = "ss",
                        instance = "ww"
                }, "value", "100");
                return "OK";
            }
            catch (Exception ex)
            {
                return ex.ToString();
            }
        }

        // Shutdown actions before closing
        public string Shutdown(string stopParam)
        {
            return "OK";
        }

        public string FromHost(string func, HAMessage channel)
        {   
            switch (func.ToUpper())
            {
                case "INI":
                    Console.WriteLine(channel.className);
                    break;
                case "VALUE":
                    break;
                case "HISTORY":
                    break;
                default:
                    break;
            }
            return "OK";
        }
    }
}