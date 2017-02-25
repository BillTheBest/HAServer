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
    //TODO: Add ini management associated with admin screens for editing (or use a text editor for the raw ini in the admin screen)

    public class SampleExtension : IExtension
    {
        // Constructor
        public string enabled;
        public string desc;

        private IPubSub _host;

        public SampleExtension(IPubSub myHost)
        {
            _host = myHost;
        }

        public string Start()
        {
            try
            {
                //_host = myHost;
                _host.Subscribe("sample2", new ChannelKey { network = "31 Needham", category = "LIGHTING", className = "CBUS", instance = "MASTERCOCOON" }, "xx");
                _host.Publish("sample1", new ChannelKey { network = "31 Needham", category = "LIGHTING", className = "CBUS", instance = "MASTERCOCOON" }, "MYSCOPE", "MYDATA");
                _host.WriteLog(Commons.LOGTYPES.INFORMATION, _host.GetIniSection("ExtensionCfg:desc"));
                //var t = 0;
                //var y = 1 / t;
                return "OK";

            }
            catch (Exception ex)
            {
                return ex.ToString();
            }
        }

        public string Stop()
        {
            return "OK";
        }
    }
}
