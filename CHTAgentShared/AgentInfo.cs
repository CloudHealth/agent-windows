using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace CloudHealth
{
    public struct AgentInfo
    {
        [JsonProperty("instance")]
        public string Instance { get; set; }
        [JsonProperty("cloud_name")]
        public string CloudName { get; set; }
        [JsonProperty("version")]
        public int Version
        {
            get
            {
                return Assembly.GetExecutingAssembly().GetName().Version.Revision;
            }
        }
        [JsonProperty("os")]
        public string OS { get { return "windows"; } }

        public static AgentInfo GetAgentInfo()
        {
            string instanceId = null;
            if (System.Diagnostics.Debugger.IsAttached)
            {
                instanceId = "i-701c521a";
            }
            else
            {
                instanceId = new EC2InstanceMetadata().InstanceId;
            }
            return new AgentInfo { Instance = instanceId, CloudName = AgentConfig.GetCloudName() };
        }
    }
}
