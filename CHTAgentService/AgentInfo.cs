using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Reflection;
using Newtonsoft.Json;

namespace CloudHealth
{
    public struct AgentInfo
    {
        [JsonProperty("instance")]
        public string Identifier { get; set; }
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
        [JsonProperty("facter")]
        public Dictionary<string, string> Facter
        {
            get
            {
                var props = new Dictionary<string, string>();
                Func<object, string> getSafeFuncValue = val =>
                {
                    try
                    {
                        return val == null ? "" : val.ToString();
                    }
                    catch
                    {
                        return "";
                    }
                };

                try
                {
                    var osClass = new ManagementClass("Win32_OperatingSystem");
                    foreach (var o in osClass.GetInstances())
                    {
                        var queryObj = (ManagementObject)o;
                        foreach (var prop in queryObj.Properties)
                            props.Add(prop.Name, getSafeFuncValue(prop.Value));
                    }
                }
                catch (ManagementException e)
                {
                    props.Add("Error", e.Message);
                }

                try
                {
                    var compClass = new ManagementClass("Win32_ComputerSystem");
                    foreach (var o in compClass.GetInstances())
                    {
                        var queryObj = (ManagementObject)o;
                        foreach (var prop in queryObj.Properties)
                            props.Add(string.Format("Computer_{0}", prop.Name), getSafeFuncValue(prop.Value));
                    }
                }
                catch (ManagementException e)
                {
                    props.Add("Computer_Error", e.Message);
                }
                return props;
            }
        }

        public static string GetMacAddress()
        {
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapterConfiguration where IPEnabled=true");
            var objects = searcher.Get().Cast<ManagementObject>();
            var mac = (from o in objects orderby o["IPConnectionMetric"] select o["MACAddress"].ToString()).FirstOrDefault();
            return mac;
        }

        public static string GetVmUUID()
        {
            var searcher = new ManagementObjectSearcher(new SelectQuery("Win32_ComputerSystemProduct"));
            var objects = searcher.Get().Cast<ManagementObject>();
            var uuid = (from o in objects select o["UUID"].ToString()).FirstOrDefault();
            return uuid;
        }
        
        public static AgentInfo GetAgentInfo()
        {
            string identifier;
            if (Debugger.IsAttached)
            {
                identifier = "i-701c521a";
            }
            else
            {
                switch (AgentConfig.GetCloudName())
                {
                    case "datacenter":
                        identifier = GetMacAddress();
                        break;
                    case "azure":
                        identifier = GetVmUUID();
                        break;
                    default:
                        identifier = new EC2InstanceMetadata().InstanceId;
                        break;
                }
            }
            return new AgentInfo { Identifier = identifier, CloudName = AgentConfig.GetCloudName() };
        }
    }
}
