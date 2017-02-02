﻿using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

// To run from the command line, type 'dotnet run' from the directory with the main project files.
// Camel case for variables, Pascal case for methods
// Add project references by including the project csproj not the DLL itself

// NUGET: Microsoft.NetCore.App 1.1.0, Microsoft.Extensions.Configuration, ..Configuratio.ini, Microsoft.Extensions.Logging

//TODO: SSL/HTTPS
//TODO: linux paths for ini file usinng Path.DirectorySeparatorCharacter
//TODO: Add sunris/sunset as extensions
//TODO: Once completed, update all the Nuget packages to the latest
//TODO: Remove system.composition nuget after testing
namespace HAServer
{
    public struct CatStruc
    {
        public string name;
        public string icon;
    }

    public class Core
    {
        static ILogger Logger { get; } = ApplicationLogging.CreateLogger<Core>();

        // Globals
        public static string networkName;
        public static List<CatStruc> categories = new List<CatStruc>();
        public static bool DebugMode = false;
        public static bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

        // Core modules
        public static PubSub pubSub;
        private static Extensions extensions;
        private static Plugins plugins;

        // Specify a different ini file on the command line for alternate configurations
        public static void Main(string[] args)
        {
            IConfigurationRoot svrCfg;
            try
            {

                // Setup logging
                ApplicationLogging.Logger.AddMyLogger();

                string thisprocessname = Process.GetCurrentProcess().ProcessName;
                if (Process.GetProcesses().Count(p => p.ProcessName == thisprocessname) > 1)
                {
                    Logger.LogCritical("HAServer is already running. End that instance before re-running. Exiting in 5 seconds...");
                    System.Threading.Thread.Sleep(5000);
                    return;
                }

                Logger.LogInformation("Automation Server starting...");

                // Get Server configuration from ini file specified on the command line or default.
                var serverIni = "HAServer.ini";                                 // Name of server config file

                if (args.Length != 0) serverIni = String.Join(" ", args);        // Spaces in filename
                if (!serverIni.ToUpper().Contains(".INI")) serverIni = serverIni + ".ini";
                serverIni = Path.Combine(Directory.GetCurrentDirectory(), serverIni);
                if (!File.Exists(serverIni))
                {
                    Logger.LogCritical("No configuration file specified or HASERVER.INI missing. Exiting");
                    ShutConsole(Consts.ExitCodes.ERR);
                }
                svrCfg = new ConfigurationBuilder()
                    .AddIniFile(serverIni, optional: false, reloadOnChange: true)
                    .Build();

                // Setup globals
                networkName = svrCfg.GetSection("Server:NetworkName").Value;
                if (networkName == null) networkName = "My Home";
                string myCat = null;
                foreach (var cat in svrCfg.GetSection("Categories").GetChildren())
                {
                    if (cat.Key.ToUpper().Contains("ICON"))
                    {
                        categories.Add(new CatStruc { name = myCat, icon = cat.Value.ToUpper() });
                    } else
                    {
                        myCat = cat.Value.ToUpper();
                    }
                }

                // Setup PubSub
                pubSub = new PubSub();

                //SQLite
                //var sqldb = new Database(svrCfg.GetSection("Database:FilesLoc").Value);

                // influxdb
                var timeSeries = new TimeSeries(svrCfg.GetSection("InfluxDB:messLogName").Value, svrCfg.GetSection("InfluxDB:adminName").Value, svrCfg.GetSection("InfluxDB:adminPwd").Value);                    //TODO: Add constructor parameters

                // Load extensions
                var extFilesLoc = svrCfg.GetSection("Server:ExtensionFilesLoc").Value;
                if (extFilesLoc == null) extFilesLoc = "extensions";
                extensions = new Extensions(Path.Combine(Directory.GetCurrentDirectory(), extFilesLoc));

                // Load plugins
                var plugFilesLoc = svrCfg.GetSection("Server:PluginFilesLoc").Value;
                if (plugFilesLoc == null) plugFilesLoc = "plugins";
                plugins = new Plugins(Path.Combine(Directory.GetCurrentDirectory(), plugFilesLoc));

                // Setup web services
                var webServices = new WebServices(svrCfg.GetSection("Server:ClientWebFilesLoc").Value);
            }
            catch (Exception ex)
            {
                Logger.LogCritical("Can't initialize HAServer. Error:" + Environment.NewLine + ex.ToString());
                ShutConsole(Consts.ExitCodes.ERR);
            }

            Logger.LogInformation("HA Console startup completed. Press 'X' to exit console.");

            // Block waiting on key input, looping until 'X' is pressed, or console shutdown (shutdown caught by closehandler
            ConsoleKey cki = 0;
            while (cki != ConsoleKey.X)
            {
                cki = Console.ReadKey(true).Key;
                // Start a console read operation. Do not display the input.
                switch (cki)
                {
                    case ConsoleKey.C:                        // Clear console screen
                        Console.Clear();
                        break;
                    case ConsoleKey.D:                             // Debug mode
                        DebugMode = !DebugMode;
                        Logger.LogInformation("Debug mode: " + DebugMode.ToString());
                        break;
                    case ConsoleKey.S:
                        //ListStateStore();
                        break;
                }
            }
            ShutConsole(Consts.ExitCodes.OK);                                       // Console shutting down, cleanup before exit
        }

        // Final routine before exiting, run cleanup and exit with an errorcode if we are called due to a fatal error
        public static void ShutConsole(Consts.ExitCodes ExitCode)
        {
            //Cleanup();
            if (ExitCode != 0)
            {
                Logger.LogCritical("Fatal errors occurred - Server stopped. Press any key to exit console.");
                Console.ReadKey(true);
            }
            Environment.Exit((int)ExitCode);
        }
    }
}