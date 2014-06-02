using System;
using System.Data;
using System.Threading;
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Core;
using log4net.Layout;
using log4net.Repository;
using NUnit.Framework;

namespace Log4Net.Async.Tests
{
    [TestFixture, Explicit]
    public class AsyncAdoAppenderTest
    {
        private const string connectionString = "Server=localhost;Integrated Security=true;Database=Logs;MultipleActiveResultSets=true;Enlist=false;Connection Timeout=10";
        private const string connectionType = "System.Data.SqlClient.SqlConnection, System.Data,Version=1.0.5000.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
        private const string commandText = "INSERT INTO Log4Net([Date], [Thread], [Level], [Logger], [Message], [Exception], [Application]) VALUES (@log_date, @thread, @log_level, @logger, @message, @exception, @application)";
        private const string ApplicationName = "TestApplication";
        private const string ErrorMessage = "TEST ERROR MESSAGE";
        private readonly Level ErrorLevel = Level.Error;
        private AsyncAdoAppender appender;
        private ILoggerRepository rep;

        [SetUp]
        public void TestFixtureSetUp()
        {
            appender = new AsyncAdoAppender
            {
                CommandType = CommandType.Text,
                ConnectionString = connectionString,
                CommandText = commandText,
                ConnectionType = connectionType
            };

            appender.AddParameter(new AdoNetAppenderParameter { DbType = DbType.DateTime, ParameterName = "@log_date", Layout = new RawUtcTimeStampLayout() });
            appender.AddParameter(new AdoNetAppenderParameter { DbType = DbType.String, Size = 255, ParameterName = "@thread", Layout = new Layout2RawLayoutAdapter(new PatternLayout("%thread")) });
            appender.AddParameter(new AdoNetAppenderParameter { DbType = DbType.String, Size = 40, ParameterName = "@log_level", Layout = new Layout2RawLayoutAdapter(new PatternLayout("%level")) });
            appender.AddParameter(new AdoNetAppenderParameter { DbType = DbType.String, Size = 255, ParameterName = "@logger", Layout = new Layout2RawLayoutAdapter(new PatternLayout("%logger")) });
            appender.AddParameter(new AdoNetAppenderParameter { DbType = DbType.String, Size = 4000, ParameterName = "@message", Layout = new Layout2RawLayoutAdapter(new PatternLayout("%message")) });
            appender.AddParameter(new AdoNetAppenderParameter { DbType = DbType.String, Size = 4000, ParameterName = "@exception", Layout = new Layout2RawLayoutAdapter(new PatternLayout("%exception")) });
            appender.AddParameter(new AdoNetAppenderParameter { DbType = DbType.String, Size = 200, ParameterName = "@application", Layout = new Layout2RawLayoutAdapter(new PatternLayout(ApplicationName)) });

            appender.Threshold = ErrorLevel;
            appender.BufferSize = -1;
            appender.ActivateOptions();

            rep = LogManager.CreateRepository(Guid.NewGuid().ToString());
            BasicConfigurator.Configure(rep, appender);
            LogsDBAccess.RemoveMatchingLogEntries(ErrorLevel, ErrorMessage, ApplicationName);
        }

        [TearDown]
        public void TestFixtureTearDown()
        {
            LogsDBAccess.RemoveMatchingLogEntries(ErrorLevel, ErrorMessage, ApplicationName);
            rep.Shutdown();
        }

        [Test]
        public void CanWriteToDatabase()
        {
            // Arrange
            ILog log = LogManager.GetLogger(rep.Name, "CanWriteToDatabase");

            // Act
            log.Error(ErrorMessage);
            Thread.Sleep(200); // let background thread finish

            // Assert
            bool isLogEntryPresent = LogsDBAccess.IsLogEntryPresent(ErrorLevel, ErrorMessage, ApplicationName);
            Assert.That(isLogEntryPresent, Is.True);
        }

        [Test]
        public void ReturnsQuicklyAfterLogging100Messages()
        {
            // Arrange
            ILog log = LogManager.GetLogger(rep.Name, "ReturnsQuicklyAfterLogging100Messages");

            // Act
            DateTime startTime = DateTime.UtcNow;
            100.Times(i => log.Error(ErrorMessage));
            DateTime endTime = DateTime.UtcNow;

            // Give background thread time to finish
            Thread.Sleep(500);

            // Assert
            Assert.That(endTime - startTime, Is.LessThan(TimeSpan.FromMilliseconds(100)));
            int logCount = LogsDBAccess.CountLogEntriesPresent(ErrorLevel, ErrorMessage, ApplicationName);
            Assert.That(logCount, Is.EqualTo(100));
        }

        [Test]
        public void CanLogAtleast1000MessagesASecond()
        {
            // Arrange
            ILog log = LogManager.GetLogger(rep.Name, "CanLogAtLeast1000MessagesASecond");

            int logCount = 0;
            bool logging = true;
            bool logsCounted = false;

            var logTimer = new Timer(s =>
            {
                logging = false;
                logCount = LogsDBAccess.CountLogEntriesPresent(ErrorLevel, ErrorMessage, ApplicationName);
                logsCounted = true;
            }, null, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(-1));

            // Act
            DateTime startTime = DateTime.UtcNow;
            while (logging)
            {
                log.Error(ErrorMessage);
            }
            TimeSpan testDuration = DateTime.UtcNow - startTime;

            while (!logsCounted)
            {
                Thread.Sleep(1);
            }

            logTimer.Dispose();

            // Assert
            var logsPerSecond = logCount / testDuration.TotalSeconds;

            Console.WriteLine("{0} messages logged in {1}s => {2}/s", logCount, testDuration.TotalSeconds, logsPerSecond);
            Assert.That(logsPerSecond, Is.GreaterThan(1000), "Must log at least 1000 messages per second");
        }
    }
}
