Log4Net.Async
=============

This library provides asynchronous Log4Net logging which can massively improve application performance if you are logging into a slow database for example. The basic concept with the library is that is uses a ring buffer with a default limit of 1000 to store pending logging events and will process them on a dedicated background thread. If the head of the buffer overtakes the tail lossy logging occurs and the oldest entries will be lost. This should never be a problem unless you are logging vast amounts of log messages to slow appenders in which case you probably need to readdress your logging.

There are 2 methods to perform async logging using this library:

1) Use an Async forwarder which wraps the existing log4net appenders and forwards their output to the async buffer [Preferred]

2) Use the AsyncAdoAppender and AsyncRollingFileAppender instead of the log4net library versions


Async Forwarder
=============
###AsyncForwardingAppender

This is easily setup and wraps existing appenders like so:

	<appender name="rollingFile" type="log4net.Appender.RollingFileAppender">
		...
	</appender>

	<appender name="asyncForwarder" type="Log4Net.Async.AsyncForwardingAppender,Log4Net.Async">
		<appender-ref ref="rollingFile" />
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