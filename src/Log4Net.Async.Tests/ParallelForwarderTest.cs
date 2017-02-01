using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Core;
using log4net.Filter;
using log4net.Repository;
using NUnit.Framework;
using Rhino.Mocks;
using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;

namespace Log4Net.Async.Tests
{
    [TestFixture]
    public class ParallelForwarderTest : IDisposable
    {
        private ParallelForwardingAppender asyncForwardingAppender;
        private DebugAppender debugAppender;
        private ILoggerRepository repository;
        private ILog log;

        [SetUp]
        public void TestFixtureSetUp()
        {
            debugAppender = new DebugAppender();
            debugAppender.ActivateOptions();

            asyncForwardingAppender = new ParallelForwardingAppender();
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
        public void NoExceptionThrownWhenCancelBeforeEndAndNoEvent()
        {
            // Arrange
            
            // Act
            LogManager.Shutdown();
            
            // Assert
            Assert.That(debugAppender.LoggedEventCount, Is.EqualTo(0), "No message or exception expected");
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
        public void WillNotOverflow()
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

            while (asyncForwardingAppender.BufferEntryCount > 0) ;
            asyncForwardingAppender.Close();

            // Assert
            Assert.That(debugAppender.LoggedEventCount, Is.EqualTo(testSize));
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
            //We can't use specific numbers here because the timing and counts will be different on different systems.
            Assert.That(numberLoggedBeforeClose, Is.GreaterThan(0), "Some number of Logging events should be logged prior to appender close.");
            //On some systems, we may not be able to flush all events prior to close, but it is reasonable to assume in this test case
            //that some events should be logged after close.
            Assert.That(numberLoggedAfterClose, Is.GreaterThan(numberLoggedBeforeClose),"Some number of LoggingEvents should be logged after close.");
            Console.WriteLine("Flushed {0} events during shutdown", numberLoggedAfterClose - numberLoggedBeforeClose);
        }

        [Test]
        public void WillResumeShutdownOnceBufferIsFlushed()
        {
            const int testSize = 10;
            const int appenderDelayMs = 100;

            // Arrange
            debugAppender.AppendDelay = TimeSpan.FromMilliseconds(appenderDelayMs);            
            // Set the delay to 10x time required to process
            asyncForwardingAppender.ShutdownFlushTimeout = (testSize * appenderDelayMs / 1000.00) * 10;

            var watch = new Stopwatch(); 

            // Act
            for (int i = 0; i < testSize; i++)
            {
                log.Error("Exception");
            }

            Thread.Sleep(50);

            watch.Start();
            asyncForwardingAppender.Close();
            watch.Stop();

            PrintShutDownTimings(testSize, watch);

            // Expect that the flushing of events/shutdown will take less configured shutdown
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(asyncForwardingAppender.ShutdownFlushTimeout * 1000.00), "Shutdown should resume immediately after appender close");
        }

        [Test]
        public void WillWaitConfiguredTimeForShutdown()
        {
            const int testSize = 50;
            const int timeoutSeconds = 2;

            // Arrange enough items to keep the queue full during shutdown
            debugAppender.AppendDelay = TimeSpan.FromMilliseconds(100);
            // Set delay that is significantly shorter than the amount of work
            asyncForwardingAppender.ShutdownFlushTimeout = timeoutSeconds;

            var watch = new Stopwatch();

            // Act
            for (int i = 0; i < testSize; i++)
            {
                log.Error("Exception");
            }

            Thread.Sleep(50);

            watch.Start();
            asyncForwardingAppender.Close();            
            watch.Stop();

            PrintShutDownTimings(testSize, watch);

            // Expect that the actual wait for timeout was not less than the configured value
            Assert.That(watch.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(asyncForwardingAppender.ShutdownFlushTimeout * 1000.00));
        }

        private void PrintShutDownTimings(int testSize, Stopwatch watch)
        {
            Console.WriteLine("Amount of work for the debug appender: {0}ms, configured flush timeout is {1}s, actual shutdown took {2}ms",
                testSize * debugAppender.AppendDelay.TotalMilliseconds, asyncForwardingAppender.ShutdownFlushTimeout, watch.ElapsedMilliseconds);
        }

        [Test, Explicit("Long-running")]
        public void WillShutdownIfBufferCannotBeFlushedFastEnough()
        {
            const int testSize = 250;

            // Arrange
            debugAppender.AppendDelay = TimeSpan.FromSeconds(1);
            Stopwatch watch = new Stopwatch();

            // Act
            for (int i = 0; i < testSize; i++)
            {
                log.Error("Exception");
            }

            Thread.Sleep(TimeSpan.FromSeconds(2));
            var numberLoggedBeforeClose = debugAppender.LoggedEventCount;

            watch.Start();
            asyncForwardingAppender.Close();
            watch.Stop();

            var numberLoggedAfterClose = debugAppender.LoggedEventCount;

            // Assert
            Assert.That(numberLoggedBeforeClose, Is.GreaterThan(0));
            Assert.That(numberLoggedAfterClose, Is.GreaterThan(numberLoggedBeforeClose));
            Assert.That(numberLoggedAfterClose, Is.LessThan(testSize));
            //We can't assume what the shutdown time will be.  It will vary from system to system. Don't test shutdown time.            
            var events = debugAppender.GetEvents();
            var evnt = events[events.Length - 1];
            Assert.That(evnt.MessageObject, Is.EqualTo("The buffer was not able to be flushed before timeout occurred."));
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
            //The identity assigned to new threads is dependent upon AppDomain principal policy.
            //Background information here:http://www.neovolve.com/post/2010/10/21/Unit-testing-a-workflow-that-relies-on-ThreadCurrentPrincipalIdentityName.aspx
            //VS2013 does have a principal assigned to new threads in the unit test.
            //It's probably best not to test that the identity has been set.
            //Assert.That(loggingEvent.Identity, Is.Empty, "Identity: always empty for some reason");
            Assert.That(loggingEvent.UserName, Is.EqualTo(currentUser == null ? String.Empty : currentUser.Name), "UserName");
            Assert.That(loggingEvent.ThreadName, Is.EqualTo(Thread.CurrentThread.Name), "ThreadName");

            Assert.That(loggingEvent.Repository, Is.Null, "Repository: Helper does not have access to this");
            Assert.That(loggingEvent.LoggerName, Is.EqualTo("TestLoggerName"), "LoggerName");

            Assert.That(loggingEvent.Level, Is.EqualTo(Level.Emergency), "Level");
            //Raised time to within 10 ms.   However, this may not be a valid test.  The time is going to vary from system to system.  The
            //tolerance setting here is arbitrary.
            Assert.That(loggingEvent.TimeStamp, Is.EqualTo(loggingTime).Within(TimeSpan.FromMilliseconds(10)), "TimeStamp");
            Assert.That(loggingEvent.ExceptionObject, Is.EqualTo(exception), "ExceptionObject");
            Assert.That(loggingEvent.MessageObject, Is.EqualTo("Who's on live support?"), "MessageObject");

            Assert.That(loggingEvent.LocationInformation.MethodName, Is.EqualTo(stackFrame.GetMethod().Name), "LocationInformation");
            Assert.That(loggingEvent.Properties["MyProperty"], Is.EqualTo("MyValue"), "Properties");
        }

        private bool _disposed = false;

        //Implement IDisposable.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (asyncForwardingAppender != null)
                    {
                        asyncForwardingAppender.Dispose();
                        asyncForwardingAppender = null;
                    }
                }
                // Free your own state (unmanaged objects).
                // Set large fields to null.
                _disposed = true;
            }
        }

        // Use C# destructor syntax for finalization code.
        ~ParallelForwarderTest()
        {
            // Simply call Dispose(false).
            Dispose(false);
        }
    }
}