using System;
using System.Diagnostics;

namespace CloudHealth
{
    public class Logger
    {
        private const string chtEventSourceName = "CHTAgent";
        private const string chtEventLogName = "CHTAgentEventLog";
        private readonly EventLog eventLog;

        public Logger()
        {
            eventLog = new EventLog();
            if (!EventLog.SourceExists(chtEventSourceName))
            {
                EventLog.CreateEventSource(chtEventSourceName, chtEventLogName);
            }
            eventLog.Source = chtEventSourceName;
            eventLog.Log = chtEventLogName;
        }

        public void LogInfo(string message, params object[] args)
        {
            var msg = string.Format(message, args);
            Console.WriteLine("INFO: {0}", msg);
            eventLog.WriteEntry(msg);
        }

        public void LogError(string message, params object[] args)
        {
            var msg = string.Format(message, args);
            Console.WriteLine("ERROR: {0}", msg);
            eventLog.WriteEntry(msg, EventLogEntryType.Error);
        }
    }
}
