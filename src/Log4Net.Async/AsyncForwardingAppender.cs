using System;
using System.Threading;
using log4net.Appender;
using log4net.Core;
using log4net.Util;

namespace Log4Net.Async
{
    public class AsyncForwardingAppender : ForwardingAppender
    {
        private const int DefaultBufferSize = 1000;
        private const FixFlags DefaultFixFlags = FixFlags.Partial;
        private static readonly TimeSpan ShutdownFlushTimeout = TimeSpan.FromSeconds(5);
        private static readonly Type ThisType = typeof(AsyncForwardingAppender);

        private readonly LoggingEventHelper loggingEventHelper = new LoggingEventHelper("AsyncForwardingAppender", DefaultFixFlags);

        private Thread forwardingThread;
        private bool shutDownRequested;

        private readonly object bufferLock = new object();
        private RingBuffer<LoggingEvent> buffer;

        private bool logBufferOverflow;
        private int bufferOverflowCounter;
        private DateTime lastLoggedBufferOverflow;

        private int bufferSize = DefaultBufferSize;
        public int? BufferSize
        {
            get { return bufferSize; }
            set { SetBufferSize(value); }
        }

        FixFlags fixFlags = DefaultFixFlags;
        public FixFlags Fix
        {
            get { return fixFlags; }
            set { SetFixFlags(value); }
        }

        #region Startup

        public override void ActivateOptions()
        {
            base.ActivateOptions();

            InitializeBuffer();
            InitializeAppenders();
            StartForwarding();
        }

        private void StartForwarding()
        {
            if (shutDownRequested)
            {
                return;
            }

            forwardingThread =
                new Thread(ForwardingThreadExecute)
                {
                    Name = String.Format("{0} Forwarding Appender Thread", Name),
                    IsBackground = false,
                };
            forwardingThread.Start();
        }
        
        #endregion
        
        #region Shutdown

        protected override void OnClose()
        {
            StopForwarding();
            base.OnClose();
        }

        private void StopForwarding()
        {
            shutDownRequested = true;
            var hasFinishedFlushingBuffer = forwardingThread.Join(ShutdownFlushTimeout);

            if (!hasFinishedFlushingBuffer)
            {
                forwardingThread.Abort();
                ForwardInternalError("Unable to flush the AsyncForwardingAppender buffer in the allotted time, forcing a shutdown", null);
            }
        }

        #endregion
        
        #region Appending

        protected override void Append(LoggingEvent loggingEvent)
        {
            if (!shutDownRequested && loggingEvent != null)
            {
                loggingEvent.Fix = fixFlags;
                buffer.Enqueue(loggingEvent);
            }
        }

        protected override void Append(LoggingEvent[] loggingEvents)
        {
            if (!shutDownRequested && loggingEvents != null)
            {
                foreach (var loggingEvent in loggingEvents)
                {
                    Append(loggingEvent);
                }
            }
        }

        #endregion
        
        #region Forwarding

        private void ForwardingThreadExecute()
        {
            while (!shutDownRequested)
            {
                try
                {
                    ForwardLoggingEventsFromBuffer();
                }
                catch (Exception exception)
                {
                    ForwardInternalError("Unexpected error in asynchronous forwarding loop", exception);
                }
            }
        }

        private void ForwardLoggingEventsFromBuffer()
        {
            LoggingEvent loggingEvent;
            while (!shutDownRequested)
            {
                if (logBufferOverflow)
                {
                    ForwardBufferOverflowError();
                    logBufferOverflow = false;
                }

                while (!buffer.TryDequeue(out loggingEvent))
                {
                    Thread.Sleep(10);
                    if (shutDownRequested)
                    {
                        break;
                    }
                }

                if (loggingEvent != null)
                {
                    ForwardLoggingEvent(loggingEvent);
                }
            }

            while (buffer.TryDequeue(out loggingEvent))
            {
                ForwardLoggingEvent(loggingEvent);
            }
        }

        private void ForwardBufferOverflowError()
        {
            ForwardInternalError(String.Format("Buffer overflow. {0} logging events have been lost in the last 30 seconds. [BufferSize: {1}]", bufferOverflowCounter, bufferSize), null);
            lastLoggedBufferOverflow = DateTime.UtcNow;
            bufferOverflowCounter = 0;
        }
        
        private void ForwardInternalError(string message, Exception exception)
        {
            LogLog.Error(ThisType, message, exception);
            var loggingEvent = loggingEventHelper.CreateLoggingEvent(Level.Error, message, exception);
            ForwardLoggingEvent(loggingEvent);
        }


        private void ForwardLoggingEvent(LoggingEvent loggingEvent)
        {
            try
            {
                base.Append(loggingEvent);
            }
            catch (Exception exception)
            {
                LogLog.Error(ThisType, "Unable to forward logging event", exception);
            }
        }

        #endregion
        
        #region Appender Management

        public override void AddAppender(IAppender newAppender)
        {
            base.AddAppender(newAppender);
            SetAppenderFixFlags(newAppender);
        }

        private void SetFixFlags(FixFlags newFixFlags)
        {
            if (newFixFlags != fixFlags)
            {
                loggingEventHelper.Fix = newFixFlags;
                fixFlags = newFixFlags;
                InitializeAppenders();
            }
        }
        
        private void InitializeAppenders()
        {
            foreach (var appender in Appenders)
            {
                SetAppenderFixFlags(appender);
            }
        }
        
        private void SetAppenderFixFlags(IAppender appender)
        {
            var bufferingAppender = appender as BufferingAppenderSkeleton;
            if (bufferingAppender != null)
            {
                bufferingAppender.Fix = Fix;
            }
        }

        #endregion

        #region Buffer Management

        private void SetBufferSize(int? newBufferSize)
        {
            lock (bufferLock)
            {
                if (newBufferSize.HasValue && newBufferSize > 0 && newBufferSize != bufferSize)
                {
                    bufferSize = newBufferSize.Value;
                    InitializeBuffer();
                }
            }
        }

        private void InitializeBuffer()
        {
            lock (bufferLock)
            {
                if (buffer == null || buffer.Size != bufferSize)
                {
                    buffer = new RingBuffer<LoggingEvent>(bufferSize);
                    buffer.BufferOverflow += OnBufferOverflow;
                }
            }
        }

        private void OnBufferOverflow(object sender, EventArgs args)
        {
            bufferOverflowCounter++;

            if (logBufferOverflow)
            {
                return;
            }

            if (lastLoggedBufferOverflow < DateTime.UtcNow.AddSeconds(-30))
            {
                logBufferOverflow = true;
            }
        }

        #endregion
    }
}
