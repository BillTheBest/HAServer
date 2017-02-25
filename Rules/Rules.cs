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

// For automatic building when changed, add the extension to the build dependencies in the solution. Then any changes to the extension source will be built before HAServer

namespace Rules
{
    public class Rules : IExtension
    {

        public static string dbName, dbLoc;

        public string enabled;
        public string desc;

        private static IPubSub _host;

        public Rules(IPubSub myHost)
        {
            _host = myHost;
        }


        // Open events database
        //public string ExtStart(IPubSub myHost)
        public string Start()
        {
            //TODO: Change these to myHost.GetIni...

            dbName = _host.GetIniSection("ExtensionCfg:DBName");
            dbLoc = _host.GetIniSection("ExtensionCfg:DBLoc");

            // Subscribe to all messages (but only in this network)
            _host.Subscribe("rules", new ChannelKey
            {
                network = Globals.networkName,
                category = "LIGHTING",
                className = "CBUS",
                instance = "MASTERCOCOON"
            });

            using (var eventsDB = new RulesDB())
            {
                eventsDB.Database.EnsureCreated();                      // Create DB file & structure if file didn't exist

                //eventsDB.Actions.Add(new Action {
                //    ActionName = "Test",
                //    ActionDescription = "My Test",
                //    ActionClass = "CBUS"
                //});

                //var count = eventsDB.SaveChanges();
                //Console.WriteLine("{0} records saved to database", count);

                //foreach (var action in eventsDB.Actions)
                //{
                //    Logger.LogInformation(" - {0}", action.ActionName);
                //}
            }


            return "OK";

        }

        public string Stop()
        {
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

        public long? ActionCategory { get; set; }

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

        public long? TrigStateCategory { get; set; }

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

        public long? TrigChgCategory { get; set; }

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
