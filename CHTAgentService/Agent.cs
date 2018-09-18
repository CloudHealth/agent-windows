using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using CloudHealth;
using RestSharp;

namespace CHTAgentService
{
    class Agent
    {
        private readonly AgentConfig agentConfig;
        private readonly Logger logger;
        private readonly API cloudHealthAPI;
        private Timer uploadTimer;
        private Timer updateTimer;
        private readonly ManualResetEvent quitEvent;
        private readonly PerformanceCollector perfMonitor;
        private int intendedExitCode;

        public Agent(string apiKey) {
            logger = new Logger();
            agentConfig = new AgentConfig();
            cloudHealthAPI = new API(agentConfig, apiKey);
            quitEvent = new ManualResetEvent(false);
            perfMonitor = new PerformanceCollector(AgentInfo.GetAgentInfo().Identifier);
            intendedExitCode = 0;
        }

        internal int Run()
        {
            logger.LogInfo("Running Version {0}", Assembly.GetExecutingAssembly().GetName().Version);
            try
            {
                logger.LogInfo("Starting Service Thread");
                StartPolling();
                logger.LogInfo("Service Thread Running");
                WaitHandle.WaitAll(new WaitHandle[] { quitEvent });
                logger.LogInfo("Stopping Thread Running");
                StopPolling();
                return intendedExitCode;
            }
            catch (Exception e)
            {
                logger.LogError("Exception: {0}", e);
                if (!Debugger.IsAttached)
                {
                    cloudHealthAPI.ReportError(e.Message, "error", e.StackTrace);
                }
                throw;
            }
        }

        internal void Stop(int exitCode = 0) {
            logger.LogInfo("Stop Called (Exit Code: {0})", exitCode);
            intendedExitCode = exitCode;
            quitEvent.Set();
        }

        protected void StartPolling()
        {
            logger.LogInfo("Starting Polling");
            uploadTimer = new Timer(OnUpload, null, 0, agentConfig.UploadInterval * 1000);
            updateTimer = new Timer(OnUpdate, null, 0, agentConfig.UpdateInterval * 1000);
            perfMonitor.Start();
        }

        private void OnUpdate(object state)
        {
            if (agentConfig.Registered)
            {
                Checkin();
            }
            else
            {
                RegisterAgent();
            }
            var updateInterval = agentConfig.UpdateInterval * 1000;
            updateTimer.Change(updateInterval, updateInterval);
        }

        private void OnUpload(object state)
        {
            UploadData();
            var uploadInterval = agentConfig.UploadInterval * 1000;
            uploadTimer.Change(uploadInterval, uploadInterval);
        }

        protected void StopPolling()
        {
            logger.LogInfo("Stopping Polling");
            perfMonitor.Stop();

            uploadTimer.Dispose();
            uploadTimer = null;

            updateTimer.Dispose();
            updateTimer = null;
        }

        protected void Checkin()
        {
            logger.LogInfo("Checking in with server");
            var agentInfo = AgentInfo.GetAgentInfo();
            cloudHealthAPI.Checkin(agentInfo, (response, agentStatus) =>
            {
                logger.LogInfo("Checkin response: {0}", response.StatusCode);
                if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created)
                {
                    logger.LogInfo("Agent Status: {0}", agentStatus);
                    agentConfig.UpdateConfiguration(agentStatus);
                    var newVersion = agentStatus.WindowsVersion.GetValueOrDefault(agentInfo.Version);
                    logger.LogInfo("Local Version: {0}, Remote Version: {1}", agentInfo.Version, newVersion);
                    if (agentInfo.Version >= newVersion) return;
                    if (agentConfig.AutoUpdate)
                    {
                        DownloadUpdate(newVersion);
                    }
                    else
                    {
                        logger.LogInfo("Skipping update because AutoUpdate is disabled");
                    }
                }
                else
                {
                    string message = response.Content.Length > 0 ? response.Content : response.ErrorMessage;
                    logger.LogError("Checkin failed with status code: {0} - {1}", response.StatusCode, message);
                }
            });
        }

        protected void RegisterAgent()
        {
            logger.LogInfo("Registering Agent with server");
            cloudHealthAPI.RegisterAgent(AgentInfo.GetAgentInfo(), (response, agentStatus) =>
            {
                if (response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK)
                {
                    logger.LogInfo("Agent registration succeeded");
                    agentConfig.UpdateConfiguration(agentStatus);  
                    agentConfig.Registered = true;
                }
                else
                {
                    string message = response.Content.Length > 0 ? response.Content : response.ErrorMessage;
                    logger.LogError("Failed to register with server: {0} - {1}", response.StatusCode, message);
                }
            });
        }

        protected void UploadData()
        {
            var appDataFolder = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            var perfDataFolder = appDataFolder.CreateSubdirectory("perfdata");

            logger.LogInfo("Uploading performance data from {0}", perfDataFolder);

            if (!agentConfig.Registered)
            {
                logger.LogError("Will not upload. agent not registered");
                return;
            }

            // Limit the number of uploads that can be initiated at the same time
            Semaphore throttle = new Semaphore(3, 3);

            foreach (var file in perfDataFolder.GetFiles("*.json"))
            {
                // wait 10 seconds for the existing uploads to finish, and exit otherwise
                if (!throttle.WaitOne(new TimeSpan(0,0,10)))
                {
                    logger.LogError("Upload is taking too long, aborting");
                    break;
                }
                logger.LogInfo("Starting upload of {0}", file.FullName);
                cloudHealthAPI.UploadPerformanceData(new StreamReader(file.OpenRead()), (response) =>
                {
                    // async operation ended, allow another one to start
                    throttle.Release();
                    if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created)
                    {
                        logger.LogInfo("Successfully uploaded {0}", file.FullName);
                        file.Delete();
                    }
                    else
                    {
                        string message = response.Content.Length > 0 ? response.Content : response.ErrorMessage;
                        logger.LogError("Failed to upload {0}: {1} - {2}", file.FullName, response.StatusCode, message);
                    }
                });
            }
        }

        protected void DownloadUpdate(int version) {
            logger.LogInfo("Downloading Version {0}", version);
            var restClient = new RestClient("https://s3.amazonaws.com/remote-collector/agent/windows");
            var urlPath = string.Format("/{0}/Update.zip", version);
            logger.LogInfo("Downloading {0}", urlPath);
            var data = restClient.DownloadData(new RestRequest(urlPath, Method.GET));
            logger.LogInfo("Downloaded {0} bytes", data.Length);

            using (var fs = new FileStream(Constants.UpdateFilePath(), FileMode.Create))
            {
                fs.Write(data, 0, data.Length);
            }
            Stop(Constants.UPDATE_EXIT_CODE);
        }
    }
}
