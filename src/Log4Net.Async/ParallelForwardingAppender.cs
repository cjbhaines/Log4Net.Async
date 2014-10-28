namespace Log4Net.Async
{
    using log4net.Core;
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// An asynchronous appender based on <see cref="BlockingCollection'T"/>
    /// </summary>
    public class ParallelForwardingAppender : AsyncForwardingAppenderBase, IDisposable
    {
        #region Private Members

        private const int DefaultBufferSize = 1000;
        private BlockingCollection<LoggingEventContext> _loggingEvents;
        private CancellationTokenSource _loggingCancelationTokenSource;
        private CancellationToken _loggingCancelationToken;
        private Task _loggingTask;
        private Double _shutdownFlushTimeout = 5;
        private TimeSpan _shutdownFlushTimespan = TimeSpan.FromSeconds(5);
        private static readonly Type ThisType = typeof(ParallelForwardingAppender);
        private volatile bool shutDownRequested;
        private int? bufferSize = DefaultBufferSize;

        #endregion Private Members

        #region Properties

        /// <summary>
        /// Gets or sets the number of LoggingEvents that will be buffered.  Set to null for unlimited.
        /// </summary>
        public override int? BufferSize
        {
            get { return bufferSize; }
            set { bufferSize = value; }
        }

        /// <summary>
        /// Gets or sets the time period in which the system will wait for appenders to flush before canceling the background task.
        /// </summary>
        public Double ShutdownFlushTimeout
        {
            get
            {
                return _shutdownFlushTimeout;
            }
            set
            {
                _shutdownFlushTimeout = value;
            }
        }

        protected override string InternalLoggerName
        {
            get
            {
                {
                    return "ParallelForwardingAppender";
                }
            }
        }

        #endregion Properties

        #region Startup

        public override void ActivateOptions()
        {
            base.ActivateOptions();
            _shutdownFlushTimespan = TimeSpan.FromSeconds(_shutdownFlushTimeout);
            StartForwarding();
        }

        private void StartForwarding()
        {
            if (shutDownRequested)
            {
                return;
            }
            //Create a collection which will block the thread and wait for new entries
            //if the collection is empty
            if (BufferSize.HasValue && BufferSize > 0)
            {
                _loggingEvents = new BlockingCollection<LoggingEventContext>(BufferSize.Value);
            }
            else
            {
                //No limit on the number of events.
                _loggingEvents = new BlockingCollection<LoggingEventContext>();
            }
            //The cancellation token is used to cancel a running task gracefully.
            _loggingCancelationTokenSource = new CancellationTokenSource();
            _loggingCancelationToken = _loggingCancelationTokenSource.Token;
            _loggingTask = new Task(SubscriberLoop, _loggingCancelationToken);
            _loggingTask.Start();
        }

        #endregion Startup

        #region Shutdown

        private void CompleteSubscriberTask()
        {
            shutDownRequested = true;
            if (_loggingEvents == null || _loggingEvents.IsAddingCompleted)
            {
                return;
            }
            //Don't allow more entries to be added.
            _loggingEvents.CompleteAdding();
            //Allow some time to flush
            Thread.Sleep(_shutdownFlushTimespan);
            //Cancel the task
            if (!_loggingCancelationToken.IsCancellationRequested)
            {
                _loggingCancelationTokenSource.Cancel();
            }
            if (!_loggingEvents.IsCompleted)
            {
                ForwardInternalError("The ParallelForwardingAppender buffer was not able to be flushed before timeout occurred.", null, ThisType);
            }
        }

        protected override void OnClose()
        {
            CompleteSubscriberTask();
            Dispose();
            base.OnClose();
        }

        #endregion Shutdown

        #region Appending

        protected override void Append(LoggingEvent loggingEvent)
        {
            if (_loggingEvents == null || _loggingEvents.IsAddingCompleted || loggingEvent == null)
            {
                return;
            }

            loggingEvent.Fix = Fix;
            _loggingEvents.Add(new LoggingEventContext(loggingEvent, HttpContext));
        }

        protected override void Append(LoggingEvent[] loggingEvents)
        {
            if (_loggingEvents == null || _loggingEvents.IsAddingCompleted || loggingEvents == null)
            {
                return;
            }

            foreach (var loggingEvent in loggingEvents)
            {
                Append(loggingEvent);
            }
        }

        #endregion Appending

        #region Forwarding

        /// <summary>
        /// Iterates over a BlockingCollection containing LoggingEvents.
        /// </summary>
        private void SubscriberLoop()
        {
            Thread.CurrentThread.Name = String.Format("{0} ParallelForwardingAppender Subscriber Task", Name);
            //The task will continue in a blocking loop until
            //the queue is marked as adding completed, or the task is canceled.
            while (!(_loggingCancelationToken.IsCancellationRequested || _loggingEvents.IsCompleted))
            {
                try
                {
                    //This call blocks until an item is available.
                    var entry = _loggingEvents.Take(_loggingCancelationToken);
                    //Check if there is an entry since the take may have been canceled.
                    if (entry != null)
                    {
                        HttpContext = entry.HttpContext;
                        ForwardLoggingEvent(entry.LoggingEvent, ThisType);
                    }
                }
                catch (ThreadAbortException ex)
                {
                    //Thread abort may occur on domain unload.
                    ForwardInternalError("Subscriber task was aborted.", ex, ThisType);
                    CompleteSubscriberTask();
                    //The exception is swallowed because we don't want the client application
                    //to halt due to a logging issue.
                }
                catch (Exception ex)
                {
                    //On exception, try to log the exception
                    ForwardInternalError("Subscriber task error in forwarding loop.", ex, ThisType);
                    CompleteSubscriberTask();
                    //The exception is swallowed because we don't want the client application
                    //to halt due to a logging issue.
                }
            }
            //The following is necessary to move the task into the canceled state,
            //otherwise it would be left in a faulted state.   A fault state
            //would indicate that the logging thread did not complete gracefully.
            if ((_loggingCancelationToken.IsCancellationRequested))
            {
                _loggingCancelationToken.ThrowIfCancellationRequested();
            }
        }

        #endregion Forwarding

        #region IDisposable Implementation

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
                    if (_loggingEvents != null)
                    {
                        _loggingEvents.Dispose();
                        _loggingEvents = null;
                    }
                    if (_loggingCancelationTokenSource != null)
                    {
                        _loggingCancelationTokenSource.Dispose();
                        _loggingCancelationTokenSource = null;
                    }
                }
                _disposed = true;
            }
        }

        // Use C# destructor syntax for finalization code.
        ~ParallelForwardingAppender()
        {
            // Simply call Dispose(false).
            Dispose(false);
        }

        #endregion IDisposable Implementation
    }
}