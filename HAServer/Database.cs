using System;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;


// NUGET: Microsoft.Data.Sqlite
// http://www.bricelam.net/2015/04/29/sqlite-on-corefx.html
// NUGET: Microsoft.EntityFrameworkCore.Sqlite

    // NOT USED

namespace HAServer
{

    public class Database
    {
        static ILogger Logger = ApplicationLogging.CreateLogger<Database>();

        public Database(string dbFileLoc)
        {
            try
            {
                Logger.LogInformation("Starting Automation database...");
                return; ////
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

        // Any shutdown code
        public void Shutdown()
        {
        }
    }
}
