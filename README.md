Log4Net.Async
=============

[![Build status](https://ci.appveyor.com/api/projects/status/fpn8apunhe0fr1y3)](https://ci.appveyor.com/project/cjbhaines/log4net-async)

This library provides asynchronous Log4Net logging.  Application performance is improved by performing message appending I/O operations on one or more background threads.

This library includes two types of forwarding appenders.  Forwarding appenders augment or otherwise alter the behavior of other appenders.  In this case logging operations are buffered or queued until the background thread is available to forward log messages to another appender or appenders.  This allows the thread that is creating log messages to continue rather than waiting on I/O operations.   

Multiple forwarding appenders may be configured, each having its own set of appenders, dedicated message queue, and background appending thread.  This enables parallel appending while maintaining message sequence.

The library also includes asynchronous appender implementations for ADO.NET and rolling log files.  However, the forwarding appenders are recommended since they provide asynchronous ability in addition to the widely used and tested capabilities of the Log4Net core appenders.

Forwarding Appenders
=============

###AsyncForwardingAppender

This appender utilizes a ring buffer with a a default limit of 1000 messages.  If the head of the buffer overtakes the tail, message loss occurs.  This behavior prioritizes application performance over logging fidelity.  The current implementation uses a 10ms polling (sleep) period on the background thread.

Configuration wraps one of more appenders as shown in the following configuration example:

	<appender name="rollingFile" type="log4net.Appender.RollingFileAppender">
		...
	</appender>
	
	<appender name="adoNet" type="log4net.Appender.AdoNetAppender">
		...
	</appender>
	
	<appender name="asyncForwarder" type="Log4Net.Async.AsyncForwardingAppender,Log4Net.Async">
		<appender-ref ref="rollingFile" />
		<appender-ref ref="adoNet" />
	</appender>

	<root>
		<appender-ref ref="asyncForwarder" />
	</root>

###ParallelForwardingAppender

This appender utilizes [System.Collections.Concurrent.BlockingCollection(T)](http://msdn.microsoft.com/en-us/library/dd267312(v=vs.100).aspx) and other facets of the [Task Parallel  Library](http://msdn.microsoft.com/en-us/library/dd460717(v=vs.100).aspx) (TPL) to implement a lossless message queue.  This implementation does not use polling but rather waits until new messages are available to append.  This results in less CPU overhead for queue polling and appending starts without having to wait on sleep expiration.

The default queue size is 1000 messages.  If the queue fills as a result of the rate of message creation exceeding the rate of appending, threads creating messages will block, as they would if they were not using the ParallelForwardingAppender.  

Configuration wraps one of more appenders as shown in the following configuration example:

	<appender name="rollingFile" type="log4net.Appender.RollingFileAppender">
		...
	</appender>
	
	<appender name="adoNet" type="log4net.Appender.AdoNetAppender">
		...
	</appender>
	
	<appender name="asyncForwarder" type="Log4Net.Async.ParallelForwardingAppender,Log4Net.Async">
		<appender-ref ref="rollingFile" />
		<appender-ref ref="adoNet" />
	</appender>

	<root>
		<appender-ref ref="asyncForwarder" />
	</root>
  
Async Appenders
=============
###AsyncAdoAppender

	<appender name="asyncAdoNet" type="Log4Net.Async.AsyncAdoAppender,Log4Net.Async">
		...
	</appender>
	
	<root>
      <appender-ref ref="asyncAdoNet" />
    </root>

###AsyncRollingFileAppender

	<appender name="asyncRollingFile" type="Log4Net.Async.AsyncRollingFileAppender,Log4Net.Async">
		...
	</appender>
	
	<root>
      <appender-ref ref="asyncRollingFile" />
    </root>


Notes
=============
If the Log4Net.Async library is only referenced in non-code (eg. a log4net log.config file), then the VS build process will not automatically copy the Log4Net.Async dll to the build folder for projects that reference other projects using Log4Net.Async. This occurs even if Copy Local is set to "True".

To get around this some of our users have created a dummy class whose sole purpose is to extend the RingBuffer class, without actually doing anything or being used anywhere at all.

If anyone has an elegant solution to this please let me know or submit a pull request.
