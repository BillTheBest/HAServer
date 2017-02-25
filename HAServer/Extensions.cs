using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using Interfaces;

// NUGET: SYstem.Runtime.Loader

// Extensions have their own section in HAServer.ini that they can retrieve but not write to.

namespace HAServer
{
    public class Extensions
    {
        static ILogger Logger = ApplicationLogging.CreateLogger<Extensions>();

        // Array of extensions loaded <name, obj>
        private ConcurrentDictionary<String, dynamic> extensions = new ConcurrentDictionary<String, dynamic>();
        public ConcurrentDictionary<String, IConfigurationRoot> extInis = new ConcurrentDictionary<string, IConfigurationRoot>();

        public Extensions(string locn)
        {
            string extName = "";
            try
            {
                if (Directory.Exists(locn))
                {
                    var extFiles =  Directory.GetFiles(locn, "*.dll");
                    if (extFiles.Length == 0)
                    {
                        Logger.LogInformation("No extensions found to load.");
                    } else
                    {
                        foreach (var assemblyPath in extFiles)
                        {
                            extName = Path.GetFileNameWithoutExtension(assemblyPath);

                            // Only load files with accompanying ini
                            if (File.Exists(locn + Path.DirectorySeparatorChar + extName + ".ini"))
                            {
                                var extCfg = new ConfigurationBuilder()
                                    .AddIniFile(locn + Path.DirectorySeparatorChar + extName + ".ini", optional: false, reloadOnChange: true)
                                    .Build();

                                if (extCfg.GetSection("ExtensionCfg:Enabled").Value != null && extCfg.GetSection("ExtensionCfg:Enabled").Value.ToUpper() == "TRUE")
                                {
                                    Logger.LogInformation("Extension " + extName.ToUpper() + " (" + extCfg.GetSection("ExtensionCfg:Desc").Value + ") enabled, loading...");
                                    var myAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
                                    var extType = myAssembly.GetType(extName + "." + extName);          
                                    if (extType != null)
                                    {
                                        extensions[extName.ToUpper()] = Activator.CreateInstance(extType, Core.pubSub) as IExtension;
                                        extInis[extName.ToUpper()] = extCfg;

                                        Thread thread = new Thread(StartExt);                           // Start on own thread
                                        thread.IsBackground = true;
                                        thread.Start(extName.ToUpper());
                                    } else
                                    {
                                        extensions[extName.ToUpper()] = null;
                                        Logger.LogWarning("Extension " + extName.ToUpper() + " not compiled with correct interface IExtension, skipping...");
                                    }
                                }
                                else
                                {
                                    Logger.LogWarning("Extension " + extName.ToUpper() + " configuration not enabled, skipping...");
                                }
                            }
                            else
                            {
                                if (extName.ToUpper() != "COMMONS") Logger.LogWarning("Extension file " + extName.ToUpper() + " does not have a INI configuration file, not loaded.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Can't load extension " + extName.ToUpper() + ", extension function won't be useable. " + Environment.NewLine + ex.ToString());
                if (ex is System.Reflection.ReflectionTypeLoadException)
                {
                    var typeLoadException = ex as ReflectionTypeLoadException;
                    var loaderExceptions = typeLoadException.LoaderExceptions;
                    Logger.LogError("Loader Exception: " + typeLoadException.LoaderExceptions.ToString());
                }

                throw ex;
            }
        }

        public bool RouteMessage(string client, Commons.HAMessage myMessage)
        {
            //var tt = 
            return true;
        }

        private void StartExt(object extName)
        {
            try
            {
                var extStart = (string)extensions[(string)extName].Start();                                   // Start with reference to pubsub instance so plugin can contact host.
                if (extStart != "OK") throw new System.MethodAccessException("Extension " + extName + "failed to start correctly, returned error: " + Environment.NewLine + extStart);
                else
                {
                    Logger.LogInformation("Extension " + extName.ToString() + " started.");
                }
            }
            catch (Exception)
            {
                Logger.LogError("Extension " + extName.ToString() + " started with errors, may not be functional");
            }
        }

        // Any shutdown code
        public void Shutdown()
        {
        }
    }
}
