using System;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

// NUGET: Microsoft.Data.Sqlite
// http://www.bricelam.net/2015/04/29/sqlite-on-corefx.html

namespace HAServer
{
    public class Database
    {
        static ILogger Logger { get; } = ApplicationLogging.CreateLogger<Database>();

        public Database(string dbFileLoc)
        {
            try
            {
                Database.Logger.LogInformation("Starting Automation database...");

                var dbName = "test";
                var dbBuild = new SqliteConnectionStringBuilder("Data Source=" + dbFileLoc + dbName + ".db");
                SqliteConnection dbConn = new SqliteConnection(dbBuild.ConnectionString);
                dbConn.Open();
                SqliteCommand sqlCmd = dbConn.CreateCommand();

                sqlCmd.CommandText = "CREATE TABLE Actions (ActionName TEXT PRIMARY KEY, ActionDescription TEXT, ActionScript TEXT, ActionScriptParam TEXT, ActionDelay INTEGER, ActionRandom BOOLEAN, ActionFunction INTEGER, ActionLogLevel INTEGER, ActionNetwork INTEGER, ActionCategory INTEGER, ActionClass TEXT, ActionInstance TEXT, ActionScope TEXT, ActionData TEXT, ActionTrigTopic BOOLEAN);";
                sqlCmd.ExecuteNonQuery();

                sqlCmd.CommandText = "SELECT * FROM MESSLOG";
                using (var reader = sqlCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var col2 = reader.GetString(2);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

    }
}
