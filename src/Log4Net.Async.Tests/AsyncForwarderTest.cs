using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Core;
using log4net.Filter;
using log4net.Repository;
using NUnit.Framework;
using Rhino.Mocks;

namespace Log4Net.Async.Tests
{
    [TestFixture]
    public class AsyncForwarderTest
    {
        private AsyncForwardingAppender asyncForwardingAppender;
        private DebugAppender debugAppender;
        private ILoggerRepository repository;
        private ILog log;

        [SetUp]
        public void TestFixtureSetUp()
        {
            debugAppender = new DebugAppender();
            debugAppender.ActivateOptions();

            asyncForwardingAppender = new AsyncForwardingAppender();
            asyncForwardingAppender.AddAppender(debugAppender);
            asyncForwardingAppender.ActivateOptions();

            repository = LogManager.CreateRepository(Guid.NewGuid().ToString());
            BasicConfigurator.Configure(repository, asyncForwardingAppender);

            log = LogManager.GetLogger(repository.Name, "TestLogger");
        }

        [TearDown]
        public void TearDown()
        {
            LogManager.Shutdown();
        }

        [Test]
        public void CanHandleNullLoggingEvent()
        {
            // Arrange

            // Act
            asyncForwardingAppender.DoAppend((LoggingEvent)null);
            log.Info("SusequentMessage");
            asyncForwardingAppender.Close();

            // Assert - should not have had an exception from previous call
            Assert.That(debugAppender.LoggedEventCount, Is.EqualTo(1), "Expected subsequent message only");
            Assert.That(debugAppender.GetEvents()[0].MessageObject, Is.EqualTo("SusequentMessage"));
        }


        [Test]
        public void CanHandleNullLoggingEvents()
        {
            // Arrange

            // Act
            asyncForwardingAppender.DoAppend((LoggingEvent[])null);
            log.Info("SusequentMessage");
            asyncForwardingAppender.Close();

            // Assert - should not have had an exception from previous call
            Assert.That(debugAppender.LoggedEventCount, Is.EqualTo(1), "Expected subsequent message only");
            Assert.That(debugAppender.GetEvents()[0].MessageObject, Is.EqualTo("SusequentMessage"));
        }


        [Test]
        public void CanHandleAppenderThrowing()
        {
            // Arrange
            var badAppender = MockRepository.GenerateMock<IAppender>();
            asyncForwardingAppender.AddAppender(badAppender);

            badAppender
                .Expect(ba => ba.DoAppend(null))
                .IgnoreArguments()
                .Throw(new Exception("Bad Appender"))
                .Repeat.Twice();

            // Act
            log.Info("InitialMessage");
            log.Info("SusequentMessage");
            asyncForwardingAppender.Close();

            // Assert
            Assert.That(debugAppender.LoggedEventCount, Is.EqualTo(2));
            Assert.That(debugAppender.GetEvents()[1].MessageObject, Is.EqualTo("SusequentMessage"));
            badAppender.VerifyAllExpectations();
        }


        [Test]
        public void WillLogFastWhenThereIsASlowAppender()
        {
            const int testSize = 1000;

            // Arrange
            debugAppender.AppendDelay = TimeSpan.FromSeconds(30);
            var watch = new Stopwatch();

            // Act
            watch.Start();
            for (int i = 0; i < testSize; i++)
            {
                log.Error("Exception");
            }
            watch.Stop();

            // Assert
            Assert.That(debugAppender.LoggedEventCount, Is.EqualTo(0));
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(testSize));
            Console.WriteLine("Logged {0} errors in {1}ms", testSize, watch.ElapsedMilliseconds);
        }


        [Test]
        public void WillLogBufferOverflowWhenItHappens()
        {
            const int testSize = 1000;

            // Arrange
            debugAppender.AppendDelay = TimeSpan.FromMilliseconds(1);
            asyncForwardingAppender.BufferSize = 100;

            // Act
            for (int i = 0; i < testSize; i++)
            {
                log.Error("Exception");
            }

            Thread.Sleep(100);
            asyncForwardingAppender.Close();

            // Assert
            var loggedEvents = debugAppender.GetEvents();
            var bufferOverflowEvents = loggedEvents
                .Where(e => e.MessageObject.ToString().Contains("Buffer overflow")).ToArray();

            Assert.That(bufferOverflowEvents, Is.Not.Empty);
            Console.WriteLine("Buffer overflow message raised {0} time(s)", bufferOverflowEvents.Length);
        }


        [Test]
        public void WillTryToFlushBufferOnShutdown()
        {
            const int testSize = 250;

            // Arrange
            debugAppender.AppendDelay = TimeSpan.FromMilliseconds(1);

            // Act
            for (int i = 0; i < testSize; i++)
            {
                log.Error("Exception");
            }

            Thread.Sleep(50);

            var numberLoggedBeforeClose = debugAppender.LoggedEventCount;
            asyncForwardingAppender.Close();
            var numberLoggedAfterClose = debugAppender.LoggedEventCount;

            // Assert
            Assert.That(numberLoggedBeforeClose, Is.LessThan(100));
            Assert.That(numberLoggedAfterClose, Is.EqualTo(testSize));
            Console.WriteLine("Flushed {0} events during shutdown", numberLoggedAfterClose - numberLoggedBeforeClose);
        }


        [Test, Explicit("Long-running")]
        public void WillShutdownIfBufferCannotBeFlushedFastEnough()
        {
            const int testSize = 250;

            // Arrange
            var watch = new Stopwatch();
            debugAppender.AppendDelay = TimeSpan.FromSeconds(1);

            // Act
            for (int i = 0; i < testSize; i++)
            {
                log.Error("Exception");
            }

            Thread.Sleep(TimeSpan.FromSeconds(2));
            var numberLoggedBeforeClose = debugAppender.LoggedEventCount;

            watch.Start();

            asyncForwardingAppender.Close();
            var numberLoggedAfterClose = debugAppender.LoggedEventCount;

            watch.Stop();

            // Assert
            Assert.That(numberLoggedBeforeClose, Is.GreaterThan(0));
            Assert.That(numberLoggedAfterClose, Is.GreaterThan(numberLoggedBeforeClose));
            Assert.That(numberLoggedAfterClose, Is.LessThan(testSize));
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(6500), "should be around 5s + the duration of the last append");

            Console.WriteLine("Flushed {0} events during shutdown which lasted {1}ms", numberLoggedAfterClose - numberLoggedBeforeClose, watch.ElapsedMilliseconds);
        }

        [Test]
        public void ThreadContextPropertiesArePreserved()
        {
            // Arrange
            ThreadContext.Properties["TestProperty"] = "My Value";
            Assert.That(asyncForwardingAppender.Fix & FixFlags.Properties, Is.EqualTo(FixFlags.Properties), "Properties must be fixed if they are to be preserved");

            // Act
            log.Info("Information");
            asyncForwardingAppender.Close();

            // Assert
            var lastLoggedEvent = debugAppender.GetEvents()[0];
            Assert.That(lastLoggedEvent.Properties["TestProperty"], Is.EqualTo("My Value"));
        }

        [Test]
        public void MessagesExcludedByFilterShouldNotBeAppended()
        {
            // Arrange
            var levelFilter =
                new LevelRangeFilter
                {
                    LevelMin = Level.Warn,
                    LevelMax = Level.Error,
                };

            asyncForwardingAppender.AddFilter(levelFilter);

            // Act
            log.Info("Info");
            log.Warn("Warn");
            log.Error("Error");
            log.Fatal("Fatal");

            asyncForwardingAppender.Close();

            //Assert
            Assert.That(debugAppender.LoggedEventCount, Is.EqualTo(2));
        }


        [Test]
        public void HelperCanGenerateLoggingEventWithAllProperties()
        {
            // Arrange
            var helper = new LoggingEventHelper("TestLoggerName", FixFlags.All);
            ThreadContext.Properties["MyProperty"] = "MyValue";
            var exception = new Exception("SomeError");

            var stackFrame = new StackFrame(0);
            var currentUser = WindowsIdentity.GetCurrent();
            var loggingTime = DateTime.Now; // Log4Net does not seem to be using UtcNow

            // Act
            var loggingEvent = helper.CreateLoggingEvent(Level.Emergency, "Who's on live support?", exception);
            Thread.Sleep(50); // to make sure the time stamp is actually captured

            // Assert
            Assert.That(loggingEvent.Domain, Is.EqualTo(AppDomain.CurrentDomain.FriendlyName), "Domain");
            Assert.That(loggingEvent.Identity, Is.Empty, "Identity: always empty for some reason");
            Assert.That(loggingEvent.UserName, Is.EqualTo(currentUser == null ? String.Empty : currentUser.Name), "UserName");
            Assert.That(loggingEvent.ThreadName, Is.EqualTo(Thread.CurrentThread.Name), "ThreadName");

            Assert.That(loggingEvent.Repository, Is.Null, "Repository: Helper does not have access to this");
            Assert.That(loggingEvent.LoggerName, Is.EqualTo("TestLoggerName"), "LoggerName");

            Assert.That(loggingEvent.Level, Is.EqualTo(Level.Emergency), "Level");
            Assert.That(loggingEvent.TimeStamp, Is.EqualTo(loggingTime).Within(TimeSpan.FromMilliseconds(5)), "TimeStamp");
            Assert.That(loggingEvent.ExceptionObject, Is.EqualTo(exception), "ExceptionObject");
            Assert.That(loggingEvent.MessageObject, Is.EqualTo("Who's on live support?"), "MessageObject");

            Assert.That(loggingEvent.LocationInformation.MethodName, Is.EqualTo(stackFrame.GetMethod().Name), "LocationInformation");
            Assert.That(loggingEvent.Properties["MyProperty"], Is.EqualTo("MyValue"), "Properties");
        }
    }
}
