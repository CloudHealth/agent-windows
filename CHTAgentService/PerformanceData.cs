using System;
using System.Collections.Generic;
using System.IO;
using CloudHealth;
using Newtonsoft.Json;

namespace CHTAgentService
{
    class PerformanceData
    {
        // Don't add a JsonProperty to this. It also has to match the required var in cp-rest-api
        public string instance;

        [JsonProperty("namespace")]
        public string NameSpace;
        [JsonProperty("start_time")]
        public DateTime StartTime;
        [JsonProperty("stop_time")]
        public DateTime StopTime;
        [JsonProperty("perf_data")]
        public List<object> PerfData;
        [JsonProperty("os")]
        public string OS { get { return AgentInfo.GetAgentInfo().OS; } }
        [JsonProperty("cloud_name")]
        public string CloudName { get { return AgentInfo.GetAgentInfo().CloudName; } }

        private readonly Logger logger;

        public PerformanceData(string instance)
        {
            logger = new Logger();
            this.instance = instance;
            var now = DateTime.UtcNow;

            // For Realz:
            var elapsedInHour = now.Millisecond + (now.Second * 1000) + (now.Minute * 60000);
            StartTime = now.AddMilliseconds(-elapsedInHour);
            StopTime = StartTime.AddMilliseconds(60 * 60 * 1000 - 1);

            // For Testing:
            //StartTime = now.AddMilliseconds(0);
            //StopTime = StartTime.AddMilliseconds(60 * 1 * 1000 - 1);

            NameSpace = "CloudHealth/Perfmon";
            PerfData = new List<object>();
        }

        public string WriteToFile()
        {
            var appDataFolder = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            var perfDataFolder = appDataFolder.CreateSubdirectory("perfdata");
            var fileName = string.Format(@"perfData-{0}.json", Guid.NewGuid());

            var filePath = perfDataFolder.FullName + "\\" + fileName;

            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(filePath, json);

            logger.LogInfo("Saved Performance Data to {0}", filePath);
            return filePath;
        }
    }
}
