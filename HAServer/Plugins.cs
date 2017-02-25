using Interfaces;
using Commons;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;

// NUGET: Microsoft.CodeAnalysis.CSharp.Scripting for 1 line scripting. Remove package if not using https://github.com/dotnet/roslyn/wiki/Scripting-API-Samples#assembly 
// NUGET: Microsoft.CodeAnalysis.CSharp 

    //TODO: Potentially make this an extension for each plugin type (eg. node, c#, python etc). These then interact with the messaging system via pub/sub

namespace HAServer
{
    public class Plugins
    {
        public static ILogger Logger = ApplicationLogging.CreateLogger<Plugins>();

        // Array of plugins loaded <name, obj>
        private ConcurrentDictionary<String, object> plugins = new ConcurrentDictionary<String, object>();

        public Plugins(string locn)
        {
            try
            {
                foreach (var cat in Globals.categories)                                                        // recurse through category directories
                {
                    Directory.CreateDirectory(Path.Combine(locn, cat.name));                                // In case of new install create dirs
                    DirectoryInfo directory = new DirectoryInfo(Path.Combine(locn, cat.name));
                    FileInfo[] plugFiles = directory.GetFiles("*.cs")                                       // Get C# files
                                                .Union(directory
                                                .GetFiles("*.js"))                                          // Get node.js files
                                                .ToArray();

                    if (plugFiles.Length != 0)
                    {
                        foreach (var plugPath in plugFiles)
                        {
                            var plugName = Path.GetFileNameWithoutExtension(plugPath.Name);

                            // Only load files with accompanying ini
                            var ini = Path.Combine(locn, cat.name, plugName + ".ini");
                            if (File.Exists(ini))
                            {
                                var plugCfg = new ConfigurationBuilder()
                                    .AddIniFile(ini, optional: false, reloadOnChange: true)
                                    .Build();

                                if (plugCfg.GetSection("PluginCfg:Enabled").Value != null && plugCfg.GetSection("PluginCfg:Enabled").Value.ToUpper() == "TRUE")
                                {
                                    switch (Path.GetExtension(plugPath.Name))
                                    {
                                        case ".cs":
                                            Logger.LogInformation("C# Plugin " + plugName.ToUpper() + " (" + plugCfg.GetSection("PluginCfg:Desc").Value + ") enabled, compiling...");
                                            var myCSPlug = new CSharp(plugPath);
                                            plugins[plugName] = myCSPlug;
                                            myCSPlug.Run();
                                            break;

                                        case ".js":
                                            Logger.LogInformation("NODE.JS Plugin " + plugName.ToUpper() + " (" + plugCfg.GetSection("PluginCfg:Desc").Value + ") enabled, running...");
                                            var myJSPlug = new NodeJS(plugPath);
                                            plugins[plugName] = myJSPlug;
                                            myJSPlug.Run();
                                            break;

                                        default:
                                            break;
                                    }

                                    // Autosubscribe to channels
                                    foreach (var section in plugCfg.GetChildren())
                                    {
                                        if (section.Key.ToUpper().StartsWith("CHANNEL"))
                                        {
                                            var plugCh = new ChannelKey
                                            {
                                                network = Globals.networkName,
                                                category = cat.name,
                                                className = plugName,
                                                instance = plugCfg.GetSection(section.Key + ":Name").Value
                                            };

                                            // Add new channel with access
                                            Core.pubSub.AddUpdChannel(plugCh, new ChannelSub
                                            {
                                                desc = plugCfg.GetSection(section.Key + ":Desc").Value,
                                                type = plugCfg.GetSection(section.Key + ":Type").Value,
                                                io = plugCfg.GetSection(section.Key + ":IO").Value,
                                                min = plugCfg.GetSection(section.Key + ":Min").Value,
                                                max = plugCfg.GetSection(section.Key + ":Max").Value,
                                                units = plugCfg.GetSection(section.Key + ":Units").Value,
                                                source = "PLUGIN",
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
                                                        name = cat.name + "." + plugName,
                                                        access = "RW"
                                                    }
                                                }
                                            });

                                            // All plugins have RW access to their channels (cat/classname) and only R access to other channels
                                            Core.pubSub.Subscribe(plugName, plugCh);
                                        }
                                    }
                                }
                                else
                                {
                                    Logger.LogWarning("Plugin " + plugName.ToUpper() + " enabled in INI file, skipping...");
                                }
                            }
                            else
                            {
                                Logger.LogWarning("Plugin file " + plugName.ToUpper() + " does not have a INI configuration file, not loaded.");
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        // Any shutdown code
        public void Shutdown()
        {
            //TODO: Close down node threads
        }
    }

    class NodeJS
    {
        FileInfo _file;

        public NodeJS(FileInfo file)
        {
            _file = file;

            string nodePath = @"C:\Program Files\nodejs";
            //var nodePath = (Core.isWindows) : @"C:\Program Files\nodejs" ?? @"LINUX PATH";         // TODO

            foreach (var item in Environment.GetEnvironmentVariable("path").Split(';'))                 // Find nodejs.exe via path
            {
                if (item.Contains("nodejs") || item.Contains("NODEJS")) nodePath = item;
            }
            //TODO: Should this be child processes for each plugin to save memory?
            var _nodeProcess = new Process
            {
                StartInfo =
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    WorkingDirectory = _file.DirectoryName,
                    FileName = Path.Combine(nodePath, "node.exe"),
                    Arguments = _file.Name
                }
            };
            _nodeProcess.EnableRaisingEvents = true;
            _nodeProcess.Start();

            _nodeProcess.OutputDataReceived += (object sender, System.Diagnostics.DataReceivedEventArgs e) =>
                Console.WriteLine(e.Data);
            _nodeProcess.BeginOutputReadLine();

            _nodeProcess.ErrorDataReceived += (object sender, System.Diagnostics.DataReceivedEventArgs e) =>
                Console.WriteLine(e.Data);
            _nodeProcess.BeginErrorReadLine();

            _nodeProcess.WaitForExit();
        }

        public object Run()
        {
            return new object();
        }
    }

    // Compile and run file
    public class CSharp
    {
        FileInfo _file;
        public object inst = null;

        public CSharp(FileInfo file)
        {
            _file = file;
        }

        public object Run()
        {
            try
            {
                var codeToCompile = File.ReadAllText(_file.FullName);
                var fileName = Path.GetFileNameWithoutExtension(_file.Name);
                SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(codeToCompile);

                string assemblyName = Path.GetRandomFileName();
                var assemblyPath = Path.GetDirectoryName(typeof(object).GetTypeInfo().Assembly.Location);                                   //The location of the .NET assemblies

                MetadataReference[] references = new MetadataReference[]
                {
                    MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "mscorlib.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Runtime.dll")),
                    MetadataReference.CreateFromFile(typeof(Commons.HAMessage).GetTypeInfo().Assembly.Location)             // Commons DLL reference 
                };

                CSharpCompilation compilation = CSharpCompilation.Create(
                    assemblyName,
                    syntaxTrees: new[] { syntaxTree },
                    references: references,
                    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

                using (var ms = new MemoryStream())
                {
                    EmitResult result = compilation.Emit(ms);

                    if (!result.Success)
                    {
                        IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
                            diagnostic.IsWarningAsError ||
                            diagnostic.Severity == DiagnosticSeverity.Error);

                        var diag = "";
                        foreach (Diagnostic diagnostic in failures)
                        {
                            diag = diag + Environment.NewLine + String.Format("\t{0}: ({1}) {2}", diagnostic.Id, diagnostic.Location.ToString(), diagnostic.GetMessage());
                        }
                        Plugins.Logger.LogError("Compilation of plugin " + fileName.ToUpper() + " failed. Plugin disabled. Error: " + diag);
                    }
                    else
                    {
                        //Plugins.Logger.LogInformation("Starting C# plugin " + _file.Name.ToUpper() + "...");
                        ms.Seek(0, SeekOrigin.Begin);

                        Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(ms);
                        var type = assembly.GetType(fileName + ".MyPlugin");
                        inst = Activator.CreateInstance(type, new object[] { Core.pubSub });       // Start with reference to pubsub instance so plugin can contact host.
                        var meth = type.GetMember("Startup").First() as MethodInfo;

                        Thread thread = new Thread(delegate ()                                                                  // Run extension on its own thread
                        {
                            var plugStart = (string)meth.Invoke(inst, new[] {"start"});
                            if (plugStart != "OK")
                            {
                                Plugins.Logger.LogWarning("C# Plugin " + fileName.ToUpper() + " started with errors, may not be functional. Error:" + Environment.NewLine + plugStart);
                                //TODO: Return errors
                            }
                            else
                            {
                                Plugins.Logger.LogInformation("C# Plugin " + fileName.ToUpper() + " started with status: " + plugStart);
                            }
                        })
                        { IsBackground = true };
                        thread.Start();
                    }
                }
                return inst;
            }
            catch (Exception ex)
            {
                Plugins.Logger.LogWarning("Cannot run plugin " + _file.Name + ", functionality won't be available. Error:" + Environment.NewLine + ex.ToString());
                return null;
            }
        }
    }
}
