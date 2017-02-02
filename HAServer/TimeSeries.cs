using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using InfluxDB.Net;
using InfluxDB.Net.Models;
using InfluxDB.Net.Enums;
using InfluxDB.Net.Infrastructure.Influx;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;


//TODO: Launch influxDB in a separate CMD process, configure with username password. Location for DB. Auto setup config when first running.
//TODO: Multithreaded
// NUGET: InfluxDB.NET.Core (1.1.22-beta)
// Set the retention property for the data - default is infinite. Look at some form of archiving each year and automatic shift of data to archive. Maybe a db instance per year, prior years will be mostly unused except for all time calcs
// Use tags for the topic structure network, class, instance but not scope & dat whihc are fields
// config file located in src/haserver (as visual studio edits files included in project in this directory.... Start with 'influxd -config <location>'


namespace HAServer
{
    public class TimeSeries
    {
        static ILogger Logger { get; } = ApplicationLogging.CreateLogger<TimeSeries>();

        const TimeUnit timeunit = TimeUnit.Milliseconds;

        public TimeSeries(string dbName, string adminName, string adminPwd)
        {
            try
            {
                Logger.LogInformation("Starting InfluxDB TimeSeries message store...");

                StartDBAsync(dbName, adminName, adminPwd);

            }
            catch (Exception ex)
            {

                throw ex;
            }
        }

        public async Task<string> StartDBAsync(string dbName, string username, string password)
        {
            try
            {
                var _client = new InfluxDb("http://localhost:8086", username, password, requestTimeout: new TimeSpan(0, 0, 5));
                // TODO: If db file does not exist then create it

                //var response = await _client.CreateDatabaseAsync(dbName);

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

        public class NewPoint
        {
            public Point values = new Point();

            public NewPoint(int network, int category, string @class, string instance, string scope, string data, DateTime timestamp)
            {
                values.Measurement = "MESSLOG";
                values.Tags = new Dictionary<string, object>
                {
                    { "NETWORK", network },               // MAYBE NETWORK IS A FIELD AS DOESNT NEED INDEXING?
                    { "CATEGORY", category },
                    { "CLASS", @class },
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
