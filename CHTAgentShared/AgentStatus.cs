using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace CloudHealth
{
    public struct AgentStatus
    {
        [JsonProperty("df_enabled")]
        public bool? DiskProfilingEnabled { get; set; }

        [JsonProperty("cpu_enabled")]
        public bool? CPUProfilingEnabled { get; set; }

        [JsonProperty("mem_enabled")]
        public bool? MemoryProfilingEnabled { get; set; }

        [JsonProperty("sample_interval")]
        public int? SampleInterval { get; set; }

        [JsonProperty("upload_interval")]
        public int? UploadInterval { get; set; }

        [JsonProperty("update_interval")]
        public int? UpdateInterval { get; set; }

        [JsonProperty("default_autoupdate")]
        public bool? DefaultAutoUpdate { get; set; }

        [JsonProperty("windows_version")]
        public int? WindowsVersion { get; set; }

        [JsonProperty("version_update_time")]
        public DateTime VersionUpdateTime { get; set; }
    }
}
