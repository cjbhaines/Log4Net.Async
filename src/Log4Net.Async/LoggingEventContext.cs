
namespace Log4Net.Async
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Log4Net;
    using log4net.Core;


    internal class LoggingEventContext
    {
        public LoggingEventContext(LoggingEvent loggingEvent, object httpContext)
        {
            LoggingEvent = loggingEvent;
            HttpContext = httpContext;
        }

        public LoggingEvent LoggingEvent { get; set; }
        public object HttpContext { get; set; }
    }
}
