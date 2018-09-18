using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CloudHealth
{
    public class Logger
    {
        private const String chtEventSourceName = "CHTAgent";
        private const String chtEventLogName = "CHTAgentEventLog";
        private System.Diagnostics.EventLog eventLog;

        public Logger()
        {
            eventLog = new System.Diagnostics.EventLog();
            if (!System.Diagnostics.EventLog.SourceExists(chtEventSourceName))
            {
                System.Diagnostics.EventLog.CreateEventSource(chtEventSourceName, chtEventLogName);
            }
            eventLog.Source = chtEventSourceName;
            eventLog.Log = chtEventLogName;
        }

        public void LogInfo(string message, params object[] args)
        {
            var msg = String.Format(message, args);
            Console.WriteLine("INFO: {0}", msg);
            eventLog.WriteEntry(msg);
        }

        public void LogError(string message, params object[] args)
        {
            var msg = String.Format(message, args);
            Console.WriteLine("ERROR: {0}", msg);
            eventLog.WriteEntry(msg, System.Diagnostics.EventLogEntryType.Error);
        }
    }
}
