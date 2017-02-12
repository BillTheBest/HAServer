using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using InfluxDB.Net;
using InfluxDB.Net.Models;
using InfluxDB.Net.Enums;
using InfluxDB.Net.Infrastructure.Influx;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Linq;
using InfluxDB.Net.Helpers;
using System.Threading;


// TODO: Edit influxdb.conf file for new data locations. 
//TODO: Multithreaded
// NUGET: InfluxDB.NET.Core (1.1.22-beta)
// Set the retention property for the data - default is infinite. Look at some form of archiving each year and automatic shift of data to archive. Maybe a db instance per year, prior years will be mostly unused except for all time calcs
// Use tags for the topic structure network, class, instance but not scope & dat whihc are fields
// config file located in src/haserver (as visual studio edits files included in project in this directory.... Start with 'influxd -config <location>'


namespace HAServer
{
    public class TimeSeries
    {
        static ILogger Logger = ApplicationLogging.CreateLogger<TimeSeries>();

        const TimeUnit timeunit = TimeUnit.Milliseconds;

        string _dbName;
        InfluxDb _client;

        public TimeSeries(string HostURL, string exeLoc, string dbName, string adminName, string adminPwd)
        {
            //try
            {
                Logger.LogInformation("Starting InfluxDB TimeSeries message store...");

                //Environment.SetEnvironmentVariable("Variable name", value, EnvironmentVariableTarget.User);

                _dbName = dbName;

                //TODO: Orderly shutdown influxdb

                var _tsProcess = new Process
                {
                    StartInfo =
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        WorkingDirectory = exeLoc,
                        FileName = Path.Combine(exeLoc, "influxd.exe"),
                        Arguments = ""
                    }
                };
                _tsProcess.EnableRaisingEvents = true;
                _tsProcess.Start();

                // Wait for Influxd to start
                //var autoEvent = new AutoResetEvent(false);
                //var InfluxReadyTimer = new Timer((Object stateInfo) =>
                //{
                //    Logger.LogInformation("Influx Started");
                //}, autoEvent, 1000, 0);

                _tsProcess.OutputDataReceived += (object sender, System.Diagnostics.DataReceivedEventArgs e) =>
                {
                    //Console.WriteLine(e.Data);
                    //InfluxReadyTimer.Change(200, 0);
                };
                _tsProcess.BeginOutputReadLine();

                _tsProcess.ErrorDataReceived += (object sender, System.Diagnostics.DataReceivedEventArgs e) =>
                {
                    //Logger.LogWarning("Errors received from InfluxDB: " + e.Data);
                };
                _tsProcess.BeginErrorReadLine();

                _client = new InfluxDb(HostURL, adminName, adminPwd, requestTimeout: new TimeSpan(0, 0, 10));
                checkDBAsync();

            }
            //catch (Exception ex)
            //{

            //  throw ex;
            // }
        }

        public async Task checkDBAsync()
        {
            var databases = await _client.ShowDatabasesAsync();
            if (!databases.Any(item => item.Name.ToUpper() == "MESSLOG")) {
                Logger.LogInformation("Creating TimeSeries database MESSLOG...");
                var createResponse = await _client.CreateDatabaseAsync("MESSLOG");
            } else
            {
                Logger.LogInformation("TimeSeries database MESSLOG open");
            }
        }


        // Redundant, used for converting from SQLite db
        public async Task<string> StartDBAsync(string dbName, string username, string password)
        {
            try
            {
                var _client = new InfluxDb("http://localhost:8086", username, password, requestTimeout: new TimeSpan(0, 0, 5));

                var dbBuild = new SqliteConnectionStringBuilder(@"Data Source=..\..\..\..\..\influxdb\" + dbName + ".db3");
                SqliteConnection dbConn = new SqliteConnection(dbBuild.ConnectionString);
                dbConn.Open();
                SqliteCommand sqlCmd = dbConn.CreateCommand();
                var count = 0;
                sqlCmd.CommandText = "SELECT * FROM MESSLOG";
                using (var reader = sqlCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (count > 12130000)
                        {
                            var myPoint = new NewPoint(1, 3, reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), new DateTime(Convert.ToInt64(reader.GetString(1))));
                            InfluxDbApiResponse writeResponse = await _client.WriteAsync(dbName, myPoint.values);
                        }
                        Console.WriteLine(count++);
                    }
                }

                //var myPoint = new newPoint(1, 3, "CBUS", "MBED_COCOON", "VALUE", "10", new DateTime(Convert.ToUInt32(reader.GetString(1))));

                List<Serie> series = await _client.QueryAsync(dbName, "select * from MESSLOG");
                Console.Write("finished");
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
                                                                       //DebugMode = !DebugMode;
                                                                       //WriteConsole(true, "Debug mode: " + DebugMode.ToString);
                            break;
                        case ConsoleKey.S:
                            //ListStateStore();
                            break;
                    }
                }

                return "OK";
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task<bool> WriteTS(Commons.HAMessage myMessage)
        {
            //var myPoint = new NewPoint(1, 3, reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), new DateTime(Convert.ToInt64(reader.GetString(1))));
            InfluxDbApiResponse writeResponse = await _client.WriteAsync(_dbName, new Point()
            {
                Measurement = "MESSLOG",
                Tags = new Dictionary<string, object>
                {
                    { "NETWORK", myMessage.network },               // MAYBE NETWORK IS A FIELD AS DOESNT NEED INDEXING?
                    { "CATEGORY", myMessage.category },
                    { "CLASS", myMessage.className },
                    { "INSTANCE", myMessage.instance}
                },
                Fields = new Dictionary<string, object>
                {
                    { "SCOPE", myMessage.scope },
                    { "DATA", myMessage.data }
                },
                Precision = timeunit
                //Timestamp = DateTime.UtcNow.ToUnixTime()
        });
            //TODO: proper return
            return true;
        }

        // Any shutdown code
        public void Shutdown()
        {
            //TODO: kill influxd process
            foreach (Process proc in Process.GetProcesses())
            {
                /*          if (FileDes == proc.MainModule.ModuleName == "influxd")
                          {
                              x.Kill();
                          }
                          */
            }
        }

        // Redundant....
        public class NewPoint
        {
            public Point values = new Point();

            public NewPoint(int network, int category, string className, string instance, string scope, string data, DateTime timestamp)
            {
                values.Measurement = "MESSLOG";
                values.Tags = new Dictionary<string, object>
                {
                    { "NETWORK", network },               // MAYBE NETWORK IS A FIELD AS DOESNT NEED INDEXING?
                    { "CATEGORY", category },
                    { "CLASS", className },
                    { "INSTANCE", instance}
                };
                values.Fields = new Dictionary<string, object>
                {
                    { "SCOPE", scope },
                    { "DATA", data }
                };
                values.Precision = timeunit;
                values.Timestamp = timestamp;
            }
        }
    }
}
