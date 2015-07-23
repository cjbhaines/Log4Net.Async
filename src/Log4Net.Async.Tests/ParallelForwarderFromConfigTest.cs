using System.Linq;
using log4net;
using log4net.Config;
using NUnit.Framework;

namespace Log4Net.Async.Tests
{
    [TestFixture]
    public class ParallelForwarderFromConfigTest
    {
        [Test]
        public void BufferSizeIsCorrectlyApplied()
        {
            // Arrange
            XmlConfigurator.Configure();
            var log = LogManager.GetLogger("TestLogger");
            
            // Act
            var appenders = log.Logger.Repository.GetAppenders();

            // Assert
            var parallelForwardingAppender = appenders.FirstOrDefault(x => x is ParallelForwardingAppender) as ParallelForwardingAppender;
            Assert.That(parallelForwardingAppender, Is.Not.Null);
            const int bufferSizeInConfigFile = 2000;
            Assert.That(parallelForwardingAppender.BufferSize, Is.EqualTo(bufferSizeInConfigFile));

            LogManager.Shutdown();
        }
    }
}
