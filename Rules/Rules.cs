using Interfaces;
using Commons;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Text;
using Newtonsoft.Json;

//NUGET: NewtonsSoft.JSon
// For automatic building when changed, add the extension to the build dependencies in the solution. Then any changes to the extension source will be built before HAServer
// Set to compile with Core Framework 1.1
// Namespace same name as plugin file name including capitalisation
// post build command: copy SampleExtension.dll ..\..\..\..\HAServer\Extensions
// Place ini file in the extensions directory of HAServer (not copied with build)
// Look to compile this as .NET core not Standard.

namespace Rules
{
    public class Rules : IExtension
    {

        public static string dbName, dbLoc;

        public string enabled;
        public string desc;

        private static IPubSub _host;
        private static int _myNetNum;

        public Rules(IPubSub myHost)
        {
            _host = myHost;
            _myNetNum = Globals.networks.IndexOf(Globals.networkName);
        }

        // Execute startup functions
        public string Start()
        {
            //Console.WriteLine("Rules: " + System.Threading.Thread.CurrentThread.ManagedThreadId.ToString());
            dbName = _host.GetIniSection("ExtensionCfg:DBName");
            dbLoc = _host.GetIniSection("ExtensionCfg:DBLoc");

            // Subscribe to all messages (but only in this network)
            _host.Subscribe("Rules", new ChannelKey
            {
                network = Globals.networkName,
                category = "",
                className = "",
                instance = ""
            });

            using (var rulesDB = new RulesDB())
            {
                rulesDB.Database.EnsureCreated();                      // Create DB file & structure if file didn't exist

                // Create admin channels for all tables
                AddTableChannel("ACTIONS");
                AddTableChannel("EVENTACTIONS");
                AddTableChannel("EVENTS");
                AddTableChannel("EVENTTRIGGERS");
                AddTableChannel("TRIGGERS");

                //rulesDB.Actions.Add(new Action
                //{
                //    ActionName = "Test",
                //    ActionDescription = "My Test",
                ///    ActionClass = "CBUS"
                //});
                //var count = rulesDB.SaveChanges();

                var testAction = new Action
                {
                    ActionCategory = "LIGHTING",
                    ActionClass = "CBUS",
                    ActionInstance = "HALLWAY",
                    ActionDelay = 0,
                    ActionDescription = "Test Lighting",
                    ActionScope = "",
                    ActionData = "100",
                    ActionRandom = false,
                    ActionTrigTopic = 1,
                    ActionLogLevel = 1,
                    ActionFunction = 1,
                    ActionName = "Turn on Hallway",
                    ActionNetwork = _myNetNum
                };

                //WHAT DOES TRIGTOPIC (ACTION TRIGGER CHANNEL) DO?

                _host.Publish("ADMIN", new ChannelKey
                {
                    network = Globals.networkName,
                    category = "SYSTEM",
                    className = "RULES",
                    instance = "ACTIONS"
                }, "ADD", JsonConvert.SerializeObject(testAction));


        //Console.WriteLine("{0} records saved to database", count);

        //foreach (var action in eventsDB.Actions)
        //{
        //    Logger.LogInformation(" - {0}", action.ActionName);
        //}
            }
            return "OK";
        }

        // Handle messages subscribed to
        public string NewMsg(string route, HAMessage message)
        {
            switch (route.ToUpper())
            {
                case "RULES":
                    ProcessRules(message);
                    break;
                case "ADMIN":
                    ProcessAdmin(message);
                    break;
                default:
                    break;
            }
            _host.WriteLog(LOGTYPES.INFORMATION, "Rules engine Got message " + message.instance.ToString());            // TEST
            return null;
        }

        // Execute any shut down functions before going offline
        public string Stop()
        {
            return "OK";
        }

        bool AddTableChannel(string tableName)
        {
            return _host.AddUpdChannel("Rules", new ChannelKey
            {
                network = Globals.networkName,
                category = "SYSTEM",
                className = "RULES",
                instance = tableName
            }, new ChannelSub
            {
                desc = "Rules table admin channel for " + tableName,
                type = "Rules Table",
                source = "ADMIN",
                auth = new List<AccessAttribs>                                  // Add default access permissions
                                                {
                                                    new AccessAttribs
                                                    {
                                                        name = "ADMINS",
                                                        access = "RW"
                                                    }
                                                }
            }, "Extensions");
        }

        string ProcessRules(HAMessage message)
        {
            return "OK";
        }

        string ProcessAdmin(HAMessage message)
        {
            switch (message.scope.ToUpper())
            {
                case "ADD":
                    var func = JsonConvert.DeserializeObject<Dictionary<string, string>>(message.data);
                    if (func.TryGetValue("ActionName", out string myVal))
                    {
                        Console.WriteLine("Name of Action: " + myVal);
                    }
                    break;
                case "GET":
                    Console.WriteLine("GET Action: " + message.data);
                    break;
                default:
                    break;
            }
            return "OK";
        }

    }

    // EF ORM structure definitions for events and triggers

    public partial class RulesDB : DbContext
    {
        public virtual DbSet<Action> Actions { get; set; }
        public virtual DbSet<EventAction> EventActions { get; set; }
        public virtual DbSet<Event> Events { get; set; }
        public virtual DbSet<EventTrigger> EventTriggers { get; set; }
        public virtual DbSet<Trigger> Triggers { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Filename=" + Rules.dbLoc + Path.DirectorySeparatorChar + Rules.dbName + ".db3");
        }
    }

    public partial class Event
    {
        [Key]
        //[StringLength(2147483647)]
        public string EventName { get; set; }

        //[StringLength(2147483647)]
        public string EventDescription { get; set; }

        public bool? EventActive { get; set; }

        public long? EventNumRecur { get; set; }

        public long? EventLastFired { get; set; }

        public bool? EventOneOff { get; set; }

        public long? EventStart { get; set; }

        public long? EventStop { get; set; }
    }

    public partial class EventAction
    {
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long ID { get; set; }

        //[StringLength(2147483647)]
        public string EventName { get; set; }

        //[StringLength(2147483647)]
        public string ActionName { get; set; }
    }

    public partial class EventTrigger
    {
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long ID { get; set; }

        //[StringLength(2147483647)]
        public string EventName { get; set; }

        //[StringLength(2147483647)]
        public string TrigName { get; set; }
    }

    public partial class Action
    {
        [Key]
        //[StringLength(2147483647)]
        public string ActionName { get; set; }

        //[StringLength(2147483647)]
        public string ActionDescription { get; set; }

        //[StringLength(2147483647)]
        public string ActionScript { get; set; }

        //[StringLength(2147483647)]
        public string ActionScriptParam { get; set; }

        public long? ActionDelay { get; set; }

        public bool? ActionRandom { get; set; }

        public long? ActionFunction { get; set; }

        public long? ActionLogLevel { get; set; }

        public long? ActionNetwork { get; set; }

        public string ActionCategory { get; set; }

        //[StringLength(2147483647)]
        public string ActionClass { get; set; }

        //[StringLength(2147483647)]
        public string ActionInstance { get; set; }

        //[StringLength(2147483647)]
        public string ActionScope { get; set; }

        //[StringLength(2147483647)]
        public string ActionData { get; set; }

        public long? ActionTrigTopic { get; set; }
    }

    public partial class Trigger
    {
        [Key]
        //[StringLength(2147483647)]
        public string TrigName { get; set; }

        //[StringLength(2147483647)]
        public string TrigDescription { get; set; }

        //[StringLength(2147483647)]
        public string TrigScript { get; set; }

        //[StringLength(2147483647)]
        public string TrigScriptParam { get; set; }

        //[StringLength(2147483647)]
        public string TrigScriptData { get; set; }

        public long? TrigScriptCond { get; set; }

        public long? TrigStateNetwork { get; set; }

        public string TrigStateCategory { get; set; }

        //[StringLength(2147483647)]
        public string TrigStateClass { get; set; }

        //[StringLength(2147483647)]
        public string TrigStateInstance { get; set; }

        //[StringLength(2147483647)]
        public string TrigStateScope { get; set; }

        //[StringLength(2147483647)]
        public string TrigStateCond { get; set; }

        //[StringLength(2147483647)]
        public string TrigStateData { get; set; }

        public long? TrigChgNetwork { get; set; }

        public string TrigChgCategory { get; set; }

        //[StringLength(2147483647)]
        public string TrigChgClass { get; set; }

        //[StringLength(2147483647)]
        public string TrigChgInstance { get; set; }

        //[StringLength(2147483647)]
        public string TrigChgScope { get; set; }

        //[StringLength(2147483647)]
        public string TrigChgCond { get; set; }

        //[StringLength(2147483647)]
        public string TrigChgData { get; set; }

        public long? TrigDateFrom { get; set; }

        public long? TrigTimeFrom { get; set; }

        public long? TrigDateTo { get; set; }

        public long? TrigTimeTo { get; set; }

        public bool? TrigFortnightly { get; set; }

        public bool? TrigMonthly { get; set; }

        public bool? TrigYearly { get; set; }

        public bool? TrigSunrise { get; set; }

        public bool? TrigSunset { get; set; }

        public bool? TrigDayTime { get; set; }

        public bool? TrigNightTime { get; set; }

        public bool? TrigMon { get; set; }

        public bool? TrigTue { get; set; }

        public bool? TrigWed { get; set; }

        public bool? TrigThu { get; set; }

        public bool? TrigFri { get; set; }

        public bool? TrigSat { get; set; }

        public bool? TrigSun { get; set; }

        public bool? TrigActive { get; set; }

        public bool? TrigInactive { get; set; }

        public long? TrigTimeofDay { get; set; }

        public long? TrigLastFired { get; set; }

        public bool? TrigChgDiff { get; set; }

        public bool? TrigChgThresh { get; set; }
    }
}
