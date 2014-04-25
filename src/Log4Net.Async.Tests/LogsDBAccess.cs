using System;
using System.Data.SqlClient;
using log4net.Core;

namespace Log4Net.Async.Tests
{
    public static class LogsDBAccess
    {
        public static string ConnectionString = "Server=localhost;Database=Logs;Application Name=TestApplication;Integrated Security=true;MultipleActiveResultSets=true";

        public static void RemoveMatchingLogEntries(Level level, string message, string application)
        {
            string commandText = String.Format("DELETE FROM dbo.Log4Net WHERE Level = @Level AND Message = @Message AND Application = @Application");

            using (SqlConnection sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                using (SqlCommand sqlCommand = new SqlCommand(commandText, sqlConnection))
                {
                    sqlCommand.Parameters.Add(new SqlParameter("Level", level.ToString()));
                    sqlCommand.Parameters.Add(new SqlParameter("Message", message));
                    sqlCommand.Parameters.Add(new SqlParameter("Application", application));
                    sqlCommand.ExecuteNonQuery();
                }
            }
        }

        public static bool IsLogEntryPresent(Level level, string message, string application)
        {
            string commandText = String.Format("SELECT * FROM dbo.Log4Net WHERE Level = @Level AND Message = @Message AND Application = @Application");

            using (SqlConnection sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                using (SqlCommand sqlCommand = new SqlCommand(commandText, sqlConnection))
                {
                    sqlCommand.Parameters.Add(new SqlParameter("Level", level.ToString()));
                    sqlCommand.Parameters.Add(new SqlParameter("Message", message));
                    sqlCommand.Parameters.Add(new SqlParameter("Application", application));
                    SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
                    int fieldCount = 0;
                    while (sqlDataReader.Read())
                    {
                        fieldCount++;
                    }
                    return fieldCount == 1;
                }
            }
        }

        public static int CountLogEntriesPresent(Level level, string message, string application)
        {
            string commandText = String.Format("SELECT COUNT(*) FROM dbo.Log4Net (nolock) WHERE Level = @Level AND Message = @Message AND Application = @Application");

            using (SqlConnection sqlConnection = new SqlConnection(ConnectionString))
            {
                sqlConnection.Open();
                using (SqlCommand sqlCommand = new SqlCommand(commandText, sqlConnection))
                {
                    sqlCommand.Parameters.Add(new SqlParameter("Level", level.ToString()));
                    sqlCommand.Parameters.Add(new SqlParameter("Message", message));
                    sqlCommand.Parameters.Add(new SqlParameter("Application", application));
                    int rowCount = (int)sqlCommand.ExecuteScalar();

                    return rowCount;
                }
            }
        }
    }
}
