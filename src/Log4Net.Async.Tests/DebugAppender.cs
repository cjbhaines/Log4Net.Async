using System;
using System.Threading;
using log4net.Appender;
using log4net.Core;

namespace Log4Net.Async.Tests
{
    public class DebugAppender : MemoryAppender
    {
        public TimeSpan AppendDelay { get; set; }
        public int LoggedEventCount { get { return m_eventsList.Count; } }

        protected override void Append(LoggingEvent loggingEvent)
        {
            if (AppendDelay > TimeSpan.Zero)
            {
                Thread.Sleep(AppendDelay);
            }
            base.Append(loggingEvent);
        }
    }
}