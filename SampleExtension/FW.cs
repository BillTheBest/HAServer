using System;
using System.Threading.Tasks;
using Interfaces;

// Framework code, don't modify
// Set to compile with Core Framework 1.1
// Namespace same name as plugin file name including capitalisation
// REMEBER TO REBUILD the plugin each time there is a change
// post build command: copy SampleExtension.dll ..\..\..\..\HAServer\Extensions
// Place ini file in the extensions directory of HAServer (not copied with build)
// Can put this into the commons DLL I think....
// Look to compile this as .NET core not Standard.
namespace SampleExtension
{
    //TODO: TRY CAYCH
    //TODO: Interface
    //TODO: Pull as much of this as possible into the core
    //TODO: Add ini management associated with admin screens for editing (or use a text editor for the raw ini in the admin screen)
    //TODO: add interfaces

    public class FW : IExtension
    {
        // Constructor
        public Type name;
        public string enabled;
        public string desc;

        private IPubSub _host;

        public FW()
        {
            name = GetType();
        }

        public string ExtStart(IPubSub myHost)
        {
            try
            {
                _host = myHost;
                _host.Publish("sample1", new ChannelKey { network = "SS", category = "LIGHTING", className = "XXX", instance = "WWW" }, "MYSCOPE", "MYDATA");
                _host.Subscribe("sample2", new ChannelKey { network = "SS", category = "LIGHTING", className = "XXX", instance = "WWW" }, "xx");
                //var t = 0;
                //var y = 1 / t;
                Task.Factory.StartNew(() => Extension.ExtensionRun("START"));          // Is task OK??
                return "OK";

            }
            catch (Exception ex)
            {
                return ex.ToString();
            }
        }

        public string ExtStop(string param)
        {
            return "stopped";
        }
    }
}
