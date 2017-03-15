using System;
using Interfaces;
using Commons;

namespace TestPlugin
{
    public class MyPlugin
    {
        private IPubSub _host;
        private ChannelKey _channel = new ChannelKey();

        public MyPlugin(IPubSub pubSub)
        {
            _host = pubSub;
            _channel.category = "LIGHTING";         // TODO get directory name Or maybe use reflection?
            _channel.className = "CBUS";      // TODO get file or namespace name
        }

        // Runs at startup
        public string Startup(string startParam)
        {
            try
            {
                //var yy = 0;
                //var tt = 1 / yy;

                // TODO: Maybe use callername instead of hard coding plugin name
                _channel.instance = "JulieWIR";
                _host.Publish(_channel, "value", "100");
                _host.WriteLog(LOGTYPES.INFORMATION, "From Test plugin");
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

        public string NewMsg(HAMessage message)
        {   
            switch (message.scope.ToUpper())
            {
                case "INI":
                    Console.WriteLine("test ini");
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