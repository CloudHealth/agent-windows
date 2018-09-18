using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using System.IO;
using System.Configuration;
using System.Collections.Specialized;
using Microsoft.Win32;

namespace CloudHealth
{
    public class AgentConfig
    {
        protected static bool warnedCloudName = false;
        protected Configuration appConfig;

        protected void ReplaceSetting(string name, string value)
        {
            appConfig.AppSettings.Settings.Remove(name);
            appConfig.AppSettings.Settings.Add(name, value);
        }
        public bool Registered
        {
            get {
                var setting = appConfig.AppSettings.Settings["registered"];
                return setting == null ? false : Convert.ToBoolean(setting.Value);
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
            get { return Convert.ToInt32(appConfig.AppSettings.Settings["uploadInterval"].Value); }
            set { ReplaceSetting("uploadInterval",  value.ToString()); Flush(); }
        }

        public Int32 UpdateInterval
        {
            get { return Convert.ToInt32(appConfig.AppSettings.Settings["updateInterval"].Value); }
            set { ReplaceSetting("updateInterval", value.ToString()); Flush(); }
        }

        public Boolean AutoUpdate
        {
            get { return Convert.ToBoolean(appConfig.AppSettings.Settings["autoUpdate"].Value); }
            set { ReplaceSetting("autoUpdate", value.ToString()); Flush(); }
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
            if (agentStatus.UploadInterval != null) { this.UploadInterval = agentStatus.UploadInterval.GetValueOrDefault(3600); }
            if (agentStatus.UpdateInterval != null) { this.UpdateInterval = agentStatus.UpdateInterval.GetValueOrDefault(300); }
            if (agentStatus.DefaultAutoUpdate != null) { this.AutoUpdate = agentStatus.DefaultAutoUpdate.GetValueOrDefault(true); }
            Flush();
        }

        private static RegistryKey CloudHealthRegistryKey()
        {
            return Registry.LocalMachine.CreateSubKey("Software\\CloudHealth Technologies");
        }

        public static string GetCloudName()
        {
            var logger = new CloudHealth.Logger();
            using (var key = CloudHealthRegistryKey())
            {
                string cloudName = (string)key.GetValue("CloudName", null);
                if (cloudName == null)
                {
                    if (!warnedCloudName)
                    {
                        logger.LogError("CloudName is null. Defaulting to \"aws\". Is CloudName value in HKEY_LOCAL_MACHINE\\Software\\CloudName Technologies set?");
                    }
                    warnedCloudName = true;
                    cloudName = "aws";
                }
                return cloudName;
            }
        }

        public static string GetAPIKey()
        {
            var logger = new CloudHealth.Logger();
            using (var key = CloudHealthRegistryKey())
            {
                string apiKey = (string)key.GetValue("AgentAPIKey", null);
                if (apiKey == null)
                {
                    logger.LogError("CHT Agent API Key is null. is AgentAPIKey value in HKEY_LOCAL_MACHINE\\Software\\CloudHealth Technologies set?");
                }

                return apiKey;
            }
        }

    }
}

