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

namespace HAServer
{
    public class Plugins
    {
        public static ILogger Logger { get; } = ApplicationLogging.CreateLogger<Plugins>();

        // Array of plugins loaded <name, obj>
        private ConcurrentDictionary<String, object> plugins = new ConcurrentDictionary<String, object>();

        public Plugins(string locn)
        {
            try
            {
                if (Directory.Exists(locn))
                {
                    foreach (var cat in Core.categories)                                                        // recurse through category directories
                    {
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
                else
                {
                    Logger.LogInformation("No plugins found - creating plugin directories...");
                    foreach (var cat in Core.categories)
                    {
                        Directory.CreateDirectory(Path.Combine(locn, cat.name));
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
        }
    }

    class NodeJS
    {
        FileInfo _file;
        object _inst = null;
        string nodePath = @"C:\Program Files\nodejs";                                      // Default

        public NodeJS(FileInfo file)
        {
            _file = file;

            foreach (var item in Environment.GetEnvironmentVariable("path").Split(';'))                 // Find nodejs.exe via path
            {
                if (item.Contains("nodejs") || item.Contains("NODEJS")) nodePath = item;
            }

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
        object _inst = null;

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
                MetadataReference[] references = new MetadataReference[]
                {
                                                    MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location)
                };

                CSharpCompilation compilation = CSharpCompilation.Create(
                    assemblyName,
                    syntaxTrees: new[] { syntaxTree },
                    references: references,
                    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

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
                            diag = diag + String.Format("\t{0}: {1}", diagnostic.Id, diagnostic.GetMessage());
                        }
                        Plugins.Logger.LogError("Compilation of plugin " + fileName.ToUpper() + " failed. Error: " + Environment.NewLine + diag);
                    }
                    else
                    {
                        Plugins.Logger.LogInformation("Starting plugin " + _file.Name.ToUpper() + "...");
                        ms.Seek(0, SeekOrigin.Begin);

                        Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(ms);
                        _inst = assembly.CreateInstance(fileName + ".MyPlugin");
                        var type = assembly.GetType(fileName + ".MyPlugin");
                        var meth = type.GetMember("Program").First() as MethodInfo;
                        Thread thread = new Thread(delegate ()                                                                  // Run extension on its own thread
                        {
                            //var plugStart = (string)extensions[extName].ExtStart(Core.pubSub);                                   // Start with reference to pubsub instance so plugin can contact host.
                            var plugStart = (string)meth.Invoke(_inst, new[] { "from C# plugin" });
                            if (plugStart != "OK")
                            {
                                Plugins.Logger.LogWarning("Plugin " + fileName.ToUpper() + " started with errors, may not be functional. Error:" + Environment.NewLine + plugStart);
                                //TODO: Return errors
                            }
                            else
                            {
                                Plugins.Logger.LogInformation("Plugin " + fileName.ToUpper() + " started with status: " + plugStart);
                            }
                        })
                        { IsBackground = true };
                        thread.Start();
                    }
                }
                return _inst;
            }
            catch (Exception ex)
            {
                Plugins.Logger.LogWarning("Cannot run plugin " + _file.Name + ", functionality won't be available. Error:" + Environment.NewLine + ex.ToString());
                return null;
            }
        }
    }
}
