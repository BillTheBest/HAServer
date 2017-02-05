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

namespace HAServer
{
    public class Extensions
    {
        static ILogger Logger { get; } = ApplicationLogging.CreateLogger<Extensions>();

        // Array of extensions loaded <name, obj>
        private ConcurrentDictionary<String, dynamic> extensions = new ConcurrentDictionary<String, dynamic>();

        public Extensions(string locn)
        {
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
                            var extName = Path.GetFileNameWithoutExtension(assemblyPath);

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
                                    //var tt = myAssembly.GetTypes();             ////////// TEST

                                    extensions[extName] = Activator.CreateInstance(myAssembly.GetType(extName + ".FW")) as IExtension;

                                    if (myAssembly.GetType(extName + ".FW") != null)
                                    {
                                        Thread thread = new Thread(delegate ()                                                                  // Run extension on its own thread
                                        {
                                            var extStart = (string)extensions[extName].ExtStart(Core.pubSub);                                   // Start with reference to pubsub instance so plugin can contact host.
                                            if (extStart != "OK")
                                            {
                                                Logger.LogError("Extension " + extName.ToUpper() + " started with errors, may not be functional" + Environment.NewLine + extStart);
                                            }
                                            else
                                            {
                                                Logger.LogInformation("Extension " + extName.ToUpper() + " started with status: " + extStart);
                                            }
                                        })
                                        { IsBackground = true };
                                        thread.Start();
                                    } else
                                    {
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
                Logger.LogError("Can't load extension, extension function won't be useable. " + Environment.NewLine + ex.ToString());
                if (ex is System.Reflection.ReflectionTypeLoadException)
                {
                    var typeLoadException = ex as ReflectionTypeLoadException;
                    var loaderExceptions = typeLoadException.LoaderExceptions;
                    Logger.LogError("Loader Exception: " + typeLoadException.LoaderExceptions.ToString());
                }

                throw ex;
            }

        }

        // Any shutdown code
        public void Shutdown()
        {
        }
    }
}
