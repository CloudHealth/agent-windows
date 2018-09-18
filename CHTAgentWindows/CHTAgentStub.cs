using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using CloudHealth;
using Ionic.Zip;

public enum ServiceState
{
    SERVICE_STOPPED = 0x00000001,
    SERVICE_START_PENDING = 0x00000002,
    SERVICE_STOP_PENDING = 0x00000003,
    SERVICE_RUNNING = 0x00000004,
    SERVICE_CONTINUE_PENDING = 0x00000005,
    SERVICE_PAUSE_PENDING = 0x00000006,
    SERVICE_PAUSED = 0x00000007,
}

[StructLayout(LayoutKind.Sequential)]
public struct ServiceStatus
{
    public long dwServiceType;
    public ServiceState dwCurrentState;
    public long dwControlsAccepted;
    public long dwWin32ExitCode;
    public long dwServiceSpecificExitCode;
    public long dwCheckPoint;
    public long dwWaitHint;
};

namespace CHTAgentWindows
{
    public partial class CHTAgentStub : ServiceBase
    {
        const double UPTIME_THRESHOLD = 10.0;

        protected Logger logger;
        private ServiceStatus serviceStatus;
        private Thread workerThread;
        private ManualResetEvent workerThreadQuitEvent;

        public CHTAgentStub()
        {
            InitializeComponent();
            serviceStatus = new ServiceStatus();
            logger = new Logger();
        }

        protected override void OnStart(string[] args)
        {
            logger.LogInfo("Starting Service");

            // Update the service state to Start Pending.
            ServiceStatus serviceStatus = new ServiceStatus();
            serviceStatus.dwCurrentState = ServiceState.SERVICE_START_PENDING;
            serviceStatus.dwWaitHint = 100000;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);

            workerThreadQuitEvent = new ManualResetEvent(false);
            workerThread = new Thread(new ParameterizedThreadStart(this.ThreadProc));
            workerThread.Start(workerThreadQuitEvent);

            // Update the service state to Running.
            serviceStatus.dwCurrentState = ServiceState.SERVICE_RUNNING;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
        }

        private void ThreadProc(object arg)
        {
            try
            {
                ManualResetEvent quitEvent = (ManualResetEvent)arg;
                var proc = Process.GetCurrentProcess();
                string executablePath = proc.Modules[0].FileName;
                string executableFolder = Path.GetDirectoryName(executablePath);
                string serviceExecutableFolder = Path.Combine(executableFolder, "CHTAgentService.exe");
                int consecutiveFailures = 0;
                int consecutiveUpdates = 0;

                var startTime = DateTime.UtcNow;
                var serviceProc = Process.Start(serviceExecutableFolder);
                while (!WaitHandle.WaitAll(new WaitHandle[] { quitEvent }, 0))
                {
                    serviceProc.WaitForExit(10000);
                    if (serviceProc.HasExited)
                    {
                        var stopTime = DateTime.UtcNow;
                        var processTime = stopTime - startTime;
                        int exitCode = serviceProc.ExitCode;
                        logger.LogInfo("Service Executable Exit Code: {0}", exitCode);
                        if (exitCode == Constants.UPDATE_EXIT_CODE)
                        {
                            PerformUpdate();
                            if (processTime.TotalSeconds < UPTIME_THRESHOLD)
                            {
                                consecutiveUpdates++;
                            }
                            else
                            {
                                consecutiveUpdates = 0;
                            }
                        }
                        else if (exitCode !=  0)
                        {
                            if (processTime.TotalSeconds < UPTIME_THRESHOLD)
                            {
                                consecutiveFailures++;
                            }
                            else
                            {
                                consecutiveFailures = 0;
                            }
                        }

                        bool delayStart = false;
                        if (consecutiveFailures > 5)
                        {
                            logger.LogError("{0} consecutive service process failures. Waiting for 30 seconds", consecutiveFailures);
                            delayStart = true;
                        }
                        else if (consecutiveUpdates > 5)
                        {
                            logger.LogError("{0} consecutive updates attempted. Waiting for 30 seconds", consecutiveUpdates);
                            delayStart = true;
                        }

                        if (delayStart)
                        {
                            WaitHandle.WaitAll(new WaitHandle[] { quitEvent }, 30000);
                        }

                        if (!WaitHandle.WaitAll(new WaitHandle[] { quitEvent }, 30000))
                        {
                            serviceProc = Process.Start(serviceExecutableFolder);
                        }
                    }
                }

                if (!serviceProc.HasExited)
                {
                    // TODO: Kill it with a signal
                    serviceProc.Kill();
                }
            }
            catch (FileNotFoundException e)
            {
                logger.LogError(String.Format("File Not Found: {0}", e.FileName));
            }
            catch (Exception e)
            {
                logger.LogError(e.ToString());
            }
        }

        protected void PerformUpdate()
        {
            logger.LogInfo("Performing Update");
            var filePath = Constants.UpdateFilePath();
            var targetDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            using (var zip = ZipFile.Read(filePath))
            {
                foreach (ZipEntry e in zip)
                {
                    e.Extract(targetDir, ExtractExistingFileAction.OverwriteSilently);
                }
            }
            File.Delete(filePath);
        } 

        protected override void OnStop()
        {
            // Update the service state to Stopping.
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOP_PENDING;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);

            logger.LogInfo("Stopping Service");
            StopServiceExecutable();

            // Update the service state to Stopping.
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOPPED;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
        }

        private void StopServiceExecutable()
        {
            workerThreadQuitEvent.Set();
            workerThread.Join();
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceStatus(IntPtr handle, ref ServiceStatus serviceStatus);
    }
}
