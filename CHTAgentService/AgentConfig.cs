using System;
using System.Configuration;
using System.Net;
using NativeRegistry;

namespace CloudHealth
{
    public class AgentConfig : IWebProxy
    {
        protected static bool warnedCloudName = false;
        protected Configuration appConfig;

        protected void ReplaceSetting(string name, string value)
        {
            appConfig.AppSettings.Settings.Remove(name);
            appConfig.AppSettings.Settings.Add(name, value);
        }

        protected string GetConfigValueOrDefault(string key, string defaultValue) 
        {
            var setting = appConfig.AppSettings.Settings[key];
            if (setting != null)
                return setting.Value;
            return defaultValue;
        }

        public bool Registered
        {
            get {
                var setting = appConfig.AppSettings.Settings["registered"];
                return setting != null && Convert.ToBoolean(setting.Value);
            }
            set {ReplaceSetting("registered", value.ToString()); Flush(); }
        }

        public string BaseURL
        {
            get { return appConfig.AppSettings.Settings["baseURL"].Value; }
            set { ReplaceSetting("baseURL", value); Flush(); }
        }

        public Int32 UploadInterval
        {
            get { return Convert.ToInt32(GetConfigValueOrDefault("uploadInterval", "3600")); }
            set { ReplaceSetting("uploadInterval",  value.ToString()); Flush(); }
        }

        public Int32 UpdateInterval
        {
            get { return Convert.ToInt32(GetConfigValueOrDefault("updateInterval", "300")); }
            set { ReplaceSetting("updateInterval", value.ToString()); Flush(); }
        }

        public Int32 CPUSampleInterval
        {
            get { return Convert.ToInt32(GetConfigValueOrDefault("cpuSampleInterval", "10")); }
            set { ReplaceSetting("cpuSampleInterval", value.ToString()); Flush(); }
        }

        public Int32 MemorySampleInterval
        {
            get { return Convert.ToInt32(GetConfigValueOrDefault("memorySampleInterval", "10")); }
            set { ReplaceSetting("memorySampleInterval", value.ToString()); Flush(); }
        }

        public Int32 FileSystemSampleInterval
        {
            // Due to poor naming in the original implementation, disk is really filesystem, but we need to keep it like this
            get { return Convert.ToInt32(GetConfigValueOrDefault("diskSampleInterval", "30")); }
            set { ReplaceSetting("diskSampleInterval", value.ToString()); Flush(); }
        }

        public Int32 DiskSampleInterval
        {
            // Due to poor naming in the original implementation, disk is really filesystem, but we need to keep it like this
            get { return Convert.ToInt32(GetConfigValueOrDefault("physicaldiskSampleInterval", "10")); }
            set { ReplaceSetting("physicaldiskSampleInterval", value.ToString()); Flush(); }
        }

        public Int32 InterfaceSampleInterval
        {
            get { return Convert.ToInt32(GetConfigValueOrDefault("interfaceSampleInterval", "10")); }
            set { ReplaceSetting("interfaceSampleInterval", value.ToString()); Flush(); }
        }

        public Int32 SampleInterval
        {
            get { return Convert.ToInt32(GetConfigValueOrDefault("sampleInterval", "10")); }
            set { ReplaceSetting("sampleInterval", value.ToString()); Flush(); }
        }

        public Boolean AutoUpdate
        {
            get { return Convert.ToBoolean(GetConfigValueOrDefault("autoUpdate", "true")); }
            set { ReplaceSetting("autoUpdate", value.ToString()); Flush(); }
        }

        public Boolean FileSystemProfilingEnabled
        {
            get { return Convert.ToBoolean(GetConfigValueOrDefault("diskProfilingEnabled", "true")); }
            set { ReplaceSetting("diskProfilingEnabled", value.ToString()); Flush(); }
        }

        public Boolean CPUProfilingEnabled
        {
            get { return Convert.ToBoolean(GetConfigValueOrDefault("cpuProfilingEnabled", "true")); }
            set { ReplaceSetting("cpuProfilingEnabled", value.ToString()); Flush(); }
        }

        public Boolean MemoryProfilingEnabled
        {
            get { return Convert.ToBoolean(GetConfigValueOrDefault("memoryProfilingEnabled", "true")); }
            set { ReplaceSetting("memoryProfilingEnabled", value.ToString()); Flush(); }
        }

        public Boolean DiskProfilingEnabled
        {
            get 
            {
                var diskAndInterfaceDefault = (GetCloudName() == "datacenter" || GetCloudName() == "azure") ? "true" : "false";
                return Convert.ToBoolean(GetConfigValueOrDefault("physicaldiskProfilingEnabled", diskAndInterfaceDefault)); 
            }
            set { ReplaceSetting("physicaldiskProfilingEnabled", value.ToString()); Flush(); }
        }

        public Boolean InterfaceProfilingEnabled
        {
            get 
            {
                var diskAndInterfaceDefault = (GetCloudName() == "datacenter" || GetCloudName() == "azure") ? "true" : "false";
                return Convert.ToBoolean(GetConfigValueOrDefault("interfaceProfilingEnabled", diskAndInterfaceDefault)); 
            }
            set { ReplaceSetting("interfaceProfilingEnabled", value.ToString()); Flush(); }
        }

        public ICredentials Credentials
        {
            get
            {
                String proxyUser = RegistryWOW6432.GetRegKey32(RegHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\CloudHealth Technologies", "ProxyUser");
                if (proxyUser == null)
                {
                    proxyUser = RegistryWOW6432.GetRegKey64(RegHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\CloudHealth Technologies", "ProxyUser");
                }
                if (proxyUser == null || proxyUser.Length == 0)
                {
                    return new NetworkCredential();
                }
                String proxyPassword = RegistryWOW6432.GetRegKey32(RegHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\CloudHealth Technologies", "ProxyPassword");
                if (proxyPassword == null)
                {
                    proxyPassword = RegistryWOW6432.GetRegKey64(RegHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\CloudHealth Technologies", "ProxyPassword");
                }
                
                return new NetworkCredential(proxyUser == null ? "" : proxyUser, proxyPassword == null ? "" : proxyPassword);
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        private void Flush() {
            appConfig.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }

        public AgentConfig()
        {
            appConfig = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
        }

        public void UpdateConfiguration(AgentStatus agentStatus)
        {
            if (agentStatus.UploadInterval != null) { UploadInterval = agentStatus.UploadInterval.GetValueOrDefault(3600); }
            if (agentStatus.UpdateInterval != null) { UpdateInterval = agentStatus.UpdateInterval.GetValueOrDefault(300); }
            
            if (agentStatus.DefaultAutoUpdate != null) { AutoUpdate = agentStatus.DefaultAutoUpdate.GetValueOrDefault(true); }

            if (agentStatus.FileSystemProfilingEnabled != null) { FileSystemProfilingEnabled = agentStatus.FileSystemProfilingEnabled.GetValueOrDefault(true); }
            if (agentStatus.CPUProfilingEnabled != null) { CPUProfilingEnabled = agentStatus.CPUProfilingEnabled.GetValueOrDefault(true); }
            if (agentStatus.MemoryProfilingEnabled != null) { MemoryProfilingEnabled = agentStatus.MemoryProfilingEnabled.GetValueOrDefault(true); }
            var diskAndInterfaceDefault = (GetCloudName() == "datacenter" || GetCloudName() == "azure");
            if (agentStatus.DiskProfilingEnabled != null) { DiskProfilingEnabled = agentStatus.DiskProfilingEnabled.GetValueOrDefault(diskAndInterfaceDefault); }
            if (agentStatus.InterfaceProfilingEnabled != null) { InterfaceProfilingEnabled = agentStatus.InterfaceProfilingEnabled.GetValueOrDefault(diskAndInterfaceDefault); }

            if (agentStatus.SampleInterval != null) { SampleInterval = agentStatus.SampleInterval.GetValueOrDefault(10); }
            if (agentStatus.CPUSampleInterval != null) { CPUSampleInterval = agentStatus.SampleInterval.GetValueOrDefault(10); }
            if (agentStatus.MemorySampleInterval != null) { MemorySampleInterval = agentStatus.SampleInterval.GetValueOrDefault(10); }
            if (agentStatus.FileSystemSampleInterval != null) { FileSystemSampleInterval = agentStatus.SampleInterval.GetValueOrDefault(30); }
            if (agentStatus.DiskSampleInterval != null) { DiskSampleInterval = agentStatus.DiskSampleInterval.GetValueOrDefault(10); }
            if (agentStatus.InterfaceSampleInterval != null) { InterfaceSampleInterval = agentStatus.InterfaceSampleInterval.GetValueOrDefault(10); }

            Flush();
        }

        public static string GetCloudName()
        {
            var logger = new Logger();
            var cloudName = (string)RegistryWOW6432.GetRegKey32(RegHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\CloudHealth Technologies", "CloudName");
            if (cloudName == null)
            {
                cloudName = (string)RegistryWOW6432.GetRegKey64(RegHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\CloudHealth Technologies", "CloudName");
            }

            if (cloudName == null)
            {
                if (!warnedCloudName)
                {
                    logger.LogError("CloudName is null. Defaulting to \"aws\". Is CloudName value in HKEY_LOCAL_MACHINE\\Software\\CloudHealth Technologies set?");
                }
                warnedCloudName = true;
                cloudName = "aws";
            }
            return cloudName;
        }

        public static string GetAPIKey()
        {
            var logger = new Logger();
            var apiKey = (string)RegistryWOW6432.GetRegKey32(RegHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\CloudHealth Technologies", "AgentAPIKey");
            if (apiKey == null)
            {
                apiKey = (string)RegistryWOW6432.GetRegKey64(RegHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\CloudHealth Technologies", "AgentAPIKey");
            }

            if (apiKey == null)
            {
                logger.LogError("CHT Agent API Key is null. is AgentAPIKey value in HKEY_LOCAL_MACHINE\\Software\\CloudHealth Technologies set?");
            }

            return apiKey;
        }

        public Uri GetProxy(Uri destination)
        {
            String proxyHost = RegistryWOW6432.GetRegKey32(RegHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\CloudHealth Technologies", "ProxyHost");
            if (proxyHost == null)
            {
                proxyHost = RegistryWOW6432.GetRegKey64(RegHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\CloudHealth Technologies", "ProxyHost");
            }

            if (proxyHost == null || proxyHost.Length == 0)
            {
                return null;
            }
            return new Uri("http://"+proxyHost);
        }

        public bool IsBypassed(Uri host)
        {
            String proxyHost = RegistryWOW6432.GetRegKey32(RegHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\CloudHealth Technologies", "ProxyHost");
            if (proxyHost == null)
            {
                proxyHost = RegistryWOW6432.GetRegKey64(RegHive.HKEY_LOCAL_MACHINE, @"SOFTWARE\CloudHealth Technologies", "ProxyHost");
            }

            // bypass proxy only if there is no proxy host set up
            return proxyHost == null || proxyHost.Length == 0;
        }
    }
}

