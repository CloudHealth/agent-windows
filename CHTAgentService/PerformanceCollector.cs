using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using CloudHealth;
using Newtonsoft.Json;
using System.Management;
using System.Net.NetworkInformation;

namespace CHTAgentService
{
    class PerformanceCollector
    {
        class Metric
        {
            protected int numSamples;
            protected float totalPerc;

            public float Min { get; protected set; }
            public float MinPerc { get; protected set; }
            public float Max { get; protected set; }
            public float MaxPerc { get; protected set; }
            public float Avg { get { return Average(Total); } }
            public float AvgPerc { get { return Average(totalPerc); } }
            public float Sum { get; protected set; }
            public float Total { get; protected set; }

            private float Average(float x)
            {
                return numSamples <= 0 ? 0.0f : x / (float)numSamples;
            }

            public Metric()
            {
                Max = MaxPerc = Total = Sum = totalPerc = numSamples = 0;
                MinPerc = Min = float.PositiveInfinity;
            }
        }

        class Gauge : Metric
        {
            /// <summary>
            /// Used to track a metric that has a fixed upper boundry, like memory, cpu, disk usage
            /// </summary>
            /// <param name="value"></param>
            /// <param name="perc"></param>
            public void AddSample(float value, float perc)
            {
                if (value < Min)
                    Min = value;

                if (perc < MinPerc)
                    MinPerc = perc;

                if (value > Max)
                    Max = value;

                if (perc > MaxPerc)
                    MaxPerc = perc;

                Total += value;
                totalPerc += perc;
                numSamples++;
            }
        }

        class Rate : Metric
        {
            /// <summary>
            /// Used to track a metric that represents rate, i.e. Foo/sec
            /// </summary>
            /// <param name="value"></param>
            /// <param name="interval"></param>
            public void AddSample(float value, int interval)
            {
                if (value < Min)
                    Min = value;

                if (value > Max)
                    Max = value;

                Total += value;
                Sum += value * interval;
                numSamples++;
            }
        }

        class Monitor<TMetaData> where TMetaData : new()
        {
            [JsonProperty("monitor_name")]
            public string monitorName;
            public readonly Dictionary<string, Metric> metrics;
            [JsonProperty("sample_specific_stats")]
            public readonly TMetaData metadata;
            [JsonProperty("timestamp")]
            public DateTime timestamp { get { return DateTime.UtcNow; } }

            public Monitor(string name)
            {
                monitorName = name;
                metrics = new Dictionary<string, Metric>();
                metadata = new TMetaData();
            }

            public T GetMetric<T>(string name) where T : Metric, new()
            {
                Metric metric;
                if (metrics.ContainsKey(name))
                    metric = metrics[name];
                else
                    metric = metrics[name] = new T();

                return (T)metric;
            }
        }

        private Timer cpuTimer;
        private Timer memoryTimer;
        private Timer filesystemTimer;
        private Timer diskTimer;
        private Timer interfaceTimer;
        private int cpuSampleInterval;
        private int memorySampleInterval;
        private int filesystemSampleInterval;
        private int diskSampleInterval;
        private int interfaceSampleInterval;

        private PerformanceCounter cpuCounter;
        private PerformanceCounter memoryCounter;
        private Dictionary<PhysicalDisk, List<PerformanceCounter>> diskCounters;
        private Dictionary<NetworkInterface, List<PerformanceCounter>> interfaceCounters;
        private ulong totalMemory;

        private Monitor<Dictionary<string, Dictionary<string, object>>> cpuMonitor;
        private Monitor<Dictionary<string, string>> memoryMonitor;
        private Dictionary<string, Monitor<Dictionary<string, string>>> filesystemMonitors;
        private Dictionary<string, Monitor<Dictionary<string, Dictionary<string, string>>>> diskMonitors;
        private Dictionary<string, Monitor<Dictionary<string, Dictionary<string, string>>>> interfaceMonitors;
        private PerformanceData currentPerformanceData;
        public string instance { get; private set; }

        private readonly Logger logger;
        private readonly AgentConfig config;

        public PerformanceCollector(string instance)
        {
            currentPerformanceData = null;
            logger = new Logger();
            this.instance = instance;
            totalMemory = GetTotalMemory();
            config = new AgentConfig();

            InitCounters();

            InitMonitors();
        }

        private void InitCounters()
        {
            try
            {
                cpuCounter = new PerformanceCounter("Processor Information", "% Processor Time", "_Total");
            }
            catch (InvalidOperationException)
            {
                // Windows 2008
                cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            }
            finally
            {
                cpuCounter.NextValue(); // first call always returns zero
            }

            memoryCounter = new PerformanceCounter("Memory", "Available Bytes");

            diskCounters = LoadPhysicalDiskCounters();

            interfaceCounters = LoadInterfaceCounters();
        }

        private void InitMonitors()
        {
            cpuMonitor = new Monitor<Dictionary<string, Dictionary<string, object>>>("cpu");
            switch (AgentConfig.GetCloudName())
            {
                case "datacenter":
                case "azure":
                    foreach(var kvp in FetchCpus())
                        cpuMonitor.metadata[kvp.Key] = kvp.Value;
                    break;
            }
            memoryMonitor = new Monitor<Dictionary<string, string>>("memory");
            diskMonitors = new Dictionary<string, Monitor<Dictionary<string, Dictionary<string, string>>>>();
            interfaceMonitors = new Dictionary<string, Monitor<Dictionary<string, Dictionary<string, string>>>>();
            filesystemMonitors = new Dictionary<string, Monitor<Dictionary<string, string>>>();
        }

        public static Dictionary<string, Dictionary<string, object>> FetchCpus()
        {
            var cpus = new Dictionary<string, Dictionary<string, object>>();
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
            {
                var objects = searcher.Get().Cast<ManagementObject>();
                
                foreach (ManagementObject obj in objects)
                {
                    var cpu = new Dictionary<string, object>() {
                        {"vendor", obj["Manufacturer"]},
                        {"family", obj["Family"]},
                        {"model_type", obj["Description"]},
                        {"model_name", obj["Name"]},
                        {"stepping", obj["Stepping"]},
                        {"microcode", obj["Revision"]},
                        {"cpu_mhz", obj["CurrentClockSpeed"]},
                        {"cache_kb", obj["L2CacheSize"] != null ? ((uint)obj["L2CacheSize"] / 1024) : 0},
                        {"bogomips", 0},
                        {"flags", ""},
                        {"architecture", obj["AddressWidth"]},
                        {"cores", obj["NumberOfCores"]},
                        {"threads", obj["NumberOfLogicalProcessors"]}
                    };
                    cpus.Add(obj["DeviceID"].ToString(), cpu);
                }
                return cpus;
            }
        }

        private Dictionary<PhysicalDisk, List<PerformanceCounter>> LoadPhysicalDiskCounters()
        {
            var instanceNames = (new PerformanceCounterCategory("PhysicalDisk")).GetInstanceNames();
            var diskCounters = new Dictionary<PhysicalDisk, List<PerformanceCounter>>();
            foreach (var disk in GetPhysicalDisks())
            {
                try
                {
                    var instanceName = instanceNames.First(n => n.StartsWith(string.Format("{0} ", disk.Index)));
                    if (instanceName != null)
                    {
                        var counters = new List<PerformanceCounter>();
                        counters.Add(new PerformanceCounter("PhysicalDisk", "Disk Reads/sec", instanceName));
                        counters.Add(new PerformanceCounter("PhysicalDisk", "Disk Writes/sec", instanceName));
                        counters.Add(new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", instanceName));
                        counters.Add(new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", instanceName));
                        diskCounters.Add(disk, counters);
                    }
                    else
                    {
                        // Can't track performance for this disk.
                        logger.LogError(string.Format("Disk {0} cannot be monitored.", disk.Name));
                    }
                }
                catch (InvalidOperationException ex)
                {
                    logger.LogError(ex.Message);
                }
            }
            return diskCounters;
        }

        private Dictionary<NetworkInterface, List<PerformanceCounter>> LoadInterfaceCounters()
        {
            var available = (new PerformanceCounterCategory("Network Interface")).GetInstanceNames();
            var counters = new Dictionary<NetworkInterface, List<PerformanceCounter>>();
            foreach (var adapter in GetNetworkInterfaces())
            {
                var formatted = adapter.Description.Replace('(', '[').Replace(')', ']').Replace('/', '_');
                if (!available.Contains(formatted))
                {
                    logger.LogError(formatted + " cannot be used for performance measurements.");
                    continue;
                }

                try
                {
                    var perfs = new List<PerformanceCounter>();
                    perfs.Add(new PerformanceCounter("Network Interface", "Bytes Received/sec", formatted));
                    perfs.Add(new PerformanceCounter("Network Interface", "Bytes Sent/sec", formatted));
                    perfs.Add(new PerformanceCounter("Network Interface", "Packets Received/sec", formatted));
                    perfs.Add(new PerformanceCounter("Network Interface", "Packets Sent/sec", formatted));
                    logger.LogInfo(formatted + " tracking performance measurements.");
                    counters.Add(adapter, perfs);
                }
                catch (InvalidOperationException ex)
                {
                    logger.LogError(ex.Message);
                }
            }
            return counters;
        }

        public void Start()
        {
            cpuSampleInterval = config.CPUSampleInterval * 1000;
            filesystemSampleInterval = config.FileSystemSampleInterval * 1000;
            memorySampleInterval = config.MemorySampleInterval * 1000;
            diskSampleInterval = config.DiskSampleInterval * 1000;
            interfaceSampleInterval = config.InterfaceSampleInterval * 1000;

            if (config.CPUProfilingEnabled)
                cpuTimer = new Timer(PollCPU, null, cpuSampleInterval, cpuSampleInterval);
            if (config.FileSystemProfilingEnabled)
                filesystemTimer = new Timer(PollFileSystems, null, filesystemSampleInterval, filesystemSampleInterval);
            if (config.MemoryProfilingEnabled)
                memoryTimer = new Timer(PollMemory, null, memorySampleInterval, memorySampleInterval);
            if (config.DiskProfilingEnabled)
                diskTimer = new Timer(PollDisks, null, diskSampleInterval, diskSampleInterval);
            if (config.InterfaceProfilingEnabled)
                interfaceTimer = new Timer(PollInterfaces, null, interfaceSampleInterval, interfaceSampleInterval);

            InitializePerformanceData();
        }

        public void Stop()
        {
            if (cpuTimer != null)
            {
                cpuTimer.Dispose();
                cpuTimer = null;
            }

            if (filesystemTimer != null)
            {
                filesystemTimer.Dispose();
                filesystemTimer = null;
            }

            if (memoryTimer != null)
            {
                memoryTimer.Dispose();
                memoryTimer = null;
            }

            if (diskTimer != null)
            {
                diskTimer.Dispose();
                diskTimer = null;
            }

            if (interfaceTimer != null)
            {
                interfaceTimer.Dispose();
                interfaceTimer = null;
            }
        }

        private void InitializePerformanceData()
        {
            if (currentPerformanceData != null)
                StorePerformanceData();

            currentPerformanceData = new PerformanceData(instance);
            Console.Write("Creating Performance Data record for {0}->{1}",
                currentPerformanceData.StartTime,
                currentPerformanceData.StopTime);

            UpdateTimerIntervals();
        }

        private void UpdateTimerIntervals()
        {
            var cpuInterval = config.CPUSampleInterval * 1000;
            var filesystemInterval = config.FileSystemSampleInterval * 1000;
            var memoryInterval = config.MemorySampleInterval * 1000;
            var diskInterval = config.DiskSampleInterval * 1000;
            var interfaceInterval = config.InterfaceSampleInterval * 1000;

            if (cpuSampleInterval != cpuInterval)
            {
                logger.LogInfo("Updating cpu sample interval from {0} to {1}", cpuSampleInterval, cpuInterval);
                cpuSampleInterval = cpuInterval;
                if (cpuTimer != null)
                    cpuTimer.Change(cpuInterval, cpuInterval);
            }

            if (filesystemSampleInterval != filesystemInterval)
            {
                logger.LogInfo("Updating filesystem sample interval from {0} to {1}", filesystemSampleInterval, filesystemInterval);
                filesystemSampleInterval = filesystemInterval;
                if (filesystemTimer != null)
                    filesystemTimer.Change(filesystemInterval, filesystemInterval);
            }

            if (memorySampleInterval != memoryInterval)
            {
                logger.LogInfo("Updating memory sample interval from {0} to {1}", memorySampleInterval, memoryInterval);
                memorySampleInterval = memoryInterval;
                if (memoryTimer != null)
                    memoryTimer.Change(memoryInterval, memoryInterval);
            }

            if (diskSampleInterval != diskInterval)
            {
                logger.LogInfo("Updating disk sample interval from {0} to {1}", diskSampleInterval, diskInterval);
                diskSampleInterval = diskInterval;
                if (diskTimer != null)
                    diskTimer.Change(diskInterval, diskInterval);
            }

            if (interfaceSampleInterval != interfaceInterval)
            {
                logger.LogInfo("Updating interface sample interval from {0} to {1}", interfaceSampleInterval, interfaceInterval);
                interfaceSampleInterval = interfaceInterval;
                if (interfaceTimer != null)
                    interfaceTimer.Change(interfaceInterval, interfaceInterval);
            }
        }

        private void PollCPU(object state)
        {
            try
            {
                var cpu = cpuCounter.NextValue();

                if (cpu >= 0)
                {
                    var freeCPU = 100.0f - cpu;

                    cpuMonitor.GetMetric<Gauge>("used").AddSample(cpu, cpu);
                    cpuMonitor.GetMetric<Gauge>("unused").AddSample(freeCPU, freeCPU);
                }
                else
                {
                    logger.LogError("Discarding invalid CPU counter: {1}", cpu);
                }

                CheckPollingTimer();
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }
        }

        private void PollMemory(object state)
        {
            try
            {
                var mem = memoryCounter.NextValue();
                var used = totalMemory - mem;

                if (mem >= 0 && used >= 0)
                {
                    memoryMonitor.GetMetric<Gauge>("free").AddSample(mem, 100.0f * (mem / totalMemory));
                    memoryMonitor.GetMetric<Gauge>("used").AddSample(used, 100.0f * (used / totalMemory));
                    CheckPollingTimer();
                }
                else
                {
                    logger.LogError("Discarding invalid memory counters: free: {1}, used: {2}, total: {3}", mem, used, totalMemory);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }
        }

        private void PollFileSystems(object state)
        {
            try
            {
                var allDrives = DriveInfo.GetDrives();

                foreach (var d in allDrives.Where(d => d.IsReady && d.DriveType != DriveType.CDRom))
                {
                    Monitor<Dictionary<string, string>> filesystemMonitor;
                    if (filesystemMonitors.ContainsKey(d.Name))
                    {
                        filesystemMonitor = filesystemMonitors[d.Name];
                    }
                    else
                    {
                        filesystemMonitors[d.Name] = filesystemMonitor = new Monitor<Dictionary<string, string>>(d.Name);
                        filesystemMonitor.metadata["fs_type"] = d.DriveType.ToString();
                        filesystemMonitor.metadata["fs_src"] = d.RootDirectory.FullName;
                    }

                    var total = (float)d.TotalSize;
                    var free = (float)d.TotalFreeSpace;
                    var used = total - free;

                    if (used >= 0 && free >= 0)
                    {
                        filesystemMonitor.GetMetric<Gauge>("used").AddSample(used, used / total);
                        filesystemMonitor.GetMetric<Gauge>("free").AddSample(free, free / total);
                    }
                    else
                    {
                        logger.LogError("Discarding invalid filesystem counter value for {4}. used: {1}, free: {2}, total:{3}", used, free, total, d.Name);
                    }
                }

                CheckPollingTimer();
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }
        }

        private void PollDisks(object state)
        {
            try
            {
                foreach (var kvp in diskCounters)
                {
                    var disk = kvp.Key;
                    var perfCounters = kvp.Value;

                    Monitor<Dictionary<string, Dictionary<string, string>>> diskMonitor;
                    if (diskMonitors.ContainsKey(disk.Name))
                    {
                        diskMonitor = diskMonitors[disk.Name];
                    }
                    else
                    {
                        diskMonitors[disk.Name] = diskMonitor = new Monitor<Dictionary<string, Dictionary<string, string>>>(disk.Name);
                        diskMonitor.metadata["info"] = new Dictionary<string, string>();
                        diskMonitor.metadata["info"]["model"] = disk.Model;
                        diskMonitor.metadata["info"]["vendor"] = disk.Vendor;
                        diskMonitor.metadata["info"]["size"] = disk.Size.ToString();
                        diskMonitor.metadata["info"]["rotational"] = disk.IsSSD ? "0" : "1";
                    }

                    foreach (var counter in perfCounters)
                    {
                        string metric = null;
                        switch (counter.CounterName)
                        {
                            case "Disk Reads/sec":
                                metric = "ops_read";
                                break;
                            case "Disk Writes/sec":
                                metric = "ops_write";
                                break;
                            case "Disk Read Bytes/sec":
                                metric = "octets_read";
                                break;
                            case "Disk Write Bytes/sec":
                                metric = "octets_write";
                                break;
                            default:
                                continue;
                        }
                        diskMonitor.GetMetric<Rate>(metric).AddSample(counter.NextValue(), config.DiskSampleInterval);
                    }
                }

                CheckPollingTimer();
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }
        }

        public static NetworkInterface[] GetNetworkInterfaces()
        {
            return NetworkInterface.GetAllNetworkInterfaces().Where((t) => !(new[] { NetworkInterfaceType.Loopback, NetworkInterfaceType.Tunnel }.Contains(t.NetworkInterfaceType))).ToArray();
        }

        public struct PhysicalDisk
        {
            public uint Index;
            public string Name;
            public string Model;
            public string Vendor;
            public ulong Size;
            public bool IsSSD;
        }

        public static PhysicalDisk[] GetPhysicalDisks()
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive"))
            {
                var objects = searcher.Get().Cast<ManagementObject>();
                var disks = new List<PhysicalDisk>();

                foreach (ManagementObject obj in objects)
                {
                    var disk = new PhysicalDisk();
                    disk.Index = (uint)obj["Index"];
                    disk.Name = (string)obj["Name"];
                    disk.Model = (string)obj["Model"];
                    disk.Vendor = (string)obj["Caption"];
                    disk.Size = (ulong)obj["Size"];
                    disk.IsSSD = false; // The code to figure this is really frickin complex, so punting for now
                    disks.Add(disk);
                }

                return disks.ToArray();
            }
        }

        private void PollInterfaces(object state)
        {
            try
            {
                foreach (var kvp in interfaceCounters)
                {

                    var adapter = kvp.Key;
                    var perfCounters = kvp.Value;

                    Monitor<Dictionary<string, Dictionary<string, string>>> interfaceMonitor;
                    if (interfaceMonitors.ContainsKey(adapter.Id))
                    {
                        interfaceMonitor = interfaceMonitors[adapter.Id];
                    }
                    else
                    {
                        interfaceMonitors[adapter.Id] = interfaceMonitor = new Monitor<Dictionary<string, Dictionary<string, string>>>(adapter.Id);
                        IPInterfaceProperties properties = adapter.GetIPProperties();
                        IPv4InterfaceProperties ipv4 = properties.GetIPv4Properties();
                        interfaceMonitor.metadata["info"] = new Dictionary<string, string>();
                        interfaceMonitor.metadata["info"]["mtu"] = ipv4.Mtu.ToString();
                        interfaceMonitor.metadata["info"]["speed"] = adapter.Speed.ToString();
                        interfaceMonitor.metadata["info"]["if_index"] = ipv4.Index.ToString();

                        foreach (var ip in properties.UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                if (ip.Address != null)
                                    interfaceMonitor.metadata["info"]["ipaddress"] = ip.Address.ToString();
                                if (ip.IPv4Mask != null)
                                    interfaceMonitor.metadata["info"]["netmask"] = ip.IPv4Mask.ToString();
                            }
                        }

                        foreach (var gateway in properties.GatewayAddresses) 
                            if (gateway.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                interfaceMonitor.metadata["info"]["network"] = gateway.Address.ToString();

                        interfaceMonitor.metadata["info"]["driver"] = adapter.Description;
                        interfaceMonitor.metadata["info"]["dev_type"] = adapter.NetworkInterfaceType.ToString();
                        interfaceMonitor.metadata["info"]["state"] = adapter.OperationalStatus.ToString();
                        interfaceMonitor.metadata["info"]["macaddress"] = string.Join(":", (from z in adapter.GetPhysicalAddress().GetAddressBytes() select z.ToString("X2")).ToArray());
                    }

                    foreach (var counter in perfCounters)
                    {
                        string metric = null;
                        switch (counter.CounterName)
                        {
                            case "Bytes Received/sec":
                                metric = "octets_rx";
                                break;
                            case "Bytes Sent/sec":
                                metric = "octets_tx";
                                break;
                            case "Packets Received/sec":
                                metric = "packets_rx";
                                break;
                            case "Packets Sent/sec":
                                metric = "packets_tx";
                                break;
                            default:
                                continue;
                        }
                        interfaceMonitor.GetMetric<Rate>(metric).AddSample(counter.NextValue(), config.InterfaceSampleInterval);
                    }
                }

                CheckPollingTimer();
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }
        }

        private Object _lockObj = new Object();
        private void CheckPollingTimer()
        {
            // Called at the end of multiple timers, so first one wins..

            if (System.Threading.Monitor.TryEnter(_lockObj))
            {
                try
                {
                    if (DateTime.UtcNow > currentPerformanceData.StopTime)
                        InitializePerformanceData();
                }
                finally
                {
                    System.Threading.Monitor.Exit(_lockObj);
                }
            }
        }

        private void StorePerformanceData()
        {
            try
            {
                var ts = DateTime.UtcNow;
                if (cpuTimer != null)
                    currentPerformanceData.PerfData.Add(CPUMonitorDataObject(ts));
                if (memoryTimer != null)
                    currentPerformanceData.PerfData.Add(MemoryMonitorDataObject(ts));
                if (filesystemTimer != null)
                    currentPerformanceData.PerfData.Add(FileSystemMonitorDataObject(ts));
                if (diskTimer != null)
                    currentPerformanceData.PerfData.Add(DiskMonitorDataObject(ts));
                if (interfaceTimer != null)
                    currentPerformanceData.PerfData.Add(InterfaceMonitorDataObject(ts));
                var filePath = currentPerformanceData.WriteToFile();
                logger.LogInfo("Wrote performance data to {0}", filePath);
            }
            finally
            {
                // Reset our Monitors because we need sample count back at 0 for the next interval
                InitMonitors();
            }
        }

        private object FileSystemMonitorDataObject(DateTime ts)
        {
            var dataObject = new Dictionary<string, object>();
            dataObject["monitor_name"] = "df";
            dataObject["timestamp"] = ts;
            var values = new Dictionary<string, object>();
            dataObject["value"] = values;

            foreach (var pair in filesystemMonitors)
            {
                var monitor = pair.Value;
                var stats = new Dictionary<string, object>();
                values[pair.Key] = stats;
                stats["sample_specific_stats"] = monitor.metadata;
                stats["used"] = new Dictionary<string, float>() 
                {
                    {"avg", monitor.GetMetric<Gauge>("used").Avg},
                    {"min", monitor.GetMetric<Gauge>("used").Min},
                    {"max", monitor.GetMetric<Gauge>("used").Max},
                };
                stats["free"] = new Dictionary<string, float>() 
                {
                    {"avg", monitor.GetMetric<Gauge>("free").Avg},
                    {"min", monitor.GetMetric<Gauge>("free").Min},
                    {"max", monitor.GetMetric<Gauge>("free").Max},
                };

            }
            return dataObject;
        }

        private object MemoryMonitorDataObject(DateTime ts)
        {
            var dataObject = new Dictionary<string, object>();
            dataObject["monitor_name"] = "memory";
            dataObject["timestamp"] = ts;
            var values = new Dictionary<string, object>();
            dataObject["value"] = values;
            values["average"] = new Dictionary<string, object>();

            var stats = new Dictionary<string, object>();
            values["average"] = stats;
            stats["sample_specific_stats"] = memoryMonitor.metadata;
            stats["used"] = new Dictionary<string, float>() 
                {
                    {"avg", memoryMonitor.GetMetric<Gauge>("used").Avg},
                    {"min", memoryMonitor.GetMetric<Gauge>("used").Min},
                    {"max", memoryMonitor.GetMetric<Gauge>("used").Max},
                    {"avg_perc", memoryMonitor.GetMetric<Gauge>("used").AvgPerc},
                    {"min_perc", memoryMonitor.GetMetric<Gauge>("used").MinPerc},
                    {"max_perc", memoryMonitor.GetMetric<Gauge>("used").MaxPerc},
                };
            stats["free"] = new Dictionary<string, float>() 
                {
                    {"avg", memoryMonitor.GetMetric<Gauge>("free").Avg},
                    {"min", memoryMonitor.GetMetric<Gauge>("free").Min},
                    {"max", memoryMonitor.GetMetric<Gauge>("free").Max},
                    {"avg_perc", memoryMonitor.GetMetric<Gauge>("free").AvgPerc},
                    {"min_perc", memoryMonitor.GetMetric<Gauge>("free").MinPerc},
                    {"max_perc", memoryMonitor.GetMetric<Gauge>("free").MaxPerc},
                };


            return dataObject;
        }

        private object CPUMonitorDataObject(DateTime ts)
        {
            var dataObject = new Dictionary<string, object>();
            dataObject["monitor_name"] = "cpu";
            dataObject["timestamp"] = ts;

            var values = new Dictionary<string, object>();
            dataObject["value"] = values;

            var stats = new Dictionary<string, object>();
            values["average"] = stats;
            stats["sample_specific_stats"] = cpuMonitor.metadata;
            stats["used"] = new Dictionary<string, float>() 
                {
                    {"avg_perc", cpuMonitor.GetMetric<Gauge>("used").AvgPerc},
                    {"min_perc", cpuMonitor.GetMetric<Gauge>("used").MinPerc},
                    {"max_perc", cpuMonitor.GetMetric<Gauge>("used").MaxPerc},
                };

            return dataObject;
        }

        private object DiskMonitorDataObject(DateTime ts)
        {
            var dataObject = new Dictionary<string, object>();
            dataObject["monitor_name"] = "disk";
            dataObject["timestamp"] = ts;

            var values = new Dictionary<string, object>();
            dataObject["value"] = values;

            foreach (var pair in diskMonitors)
            {
                var monitor = pair.Value;
                var stats = new Dictionary<string, object>();
                values[pair.Key] = stats;
                stats["sample_specific_stats"] = monitor.metadata;
                foreach (var metric in monitor.metrics)
                {
                    stats[metric.Key] = new Dictionary<string, float>() 
                        {
                            {"avg", metric.Value.Avg},
                            {"min", metric.Value.Min},
                            {"max", metric.Value.Max},
                            {"sum", metric.Value.Sum},
                        };
                }
            }

            return dataObject;
        }

        private object InterfaceMonitorDataObject(DateTime ts)
        {
            var dataObject = new Dictionary<string, object>();
            dataObject["monitor_name"] = "interface";
            dataObject["timestamp"] = ts;

            var values = new Dictionary<string, object>();
            dataObject["value"] = values;

            foreach (var pair in interfaceMonitors)
            {
                var monitor = pair.Value;
                var stats = new Dictionary<string, object>();
                values[pair.Key] = stats;
                stats["sample_specific_stats"] = monitor.metadata;
                foreach (var metric in monitor.metrics)
                {
                    stats[metric.Key] = new Dictionary<string, float>() 
                        {
                            {"avg", metric.Value.Avg},
                            {"min", metric.Value.Min},
                            {"max", metric.Value.Max},
                            {"sum", metric.Value.Sum},
                        };
                }
            }
            
            return dataObject;
        }

        private ulong GetTotalMemory()
        {
            ulong installedMemory = 0;
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                installedMemory = memStatus.ullTotalPhys;
            }
            return installedMemory;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }


        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
    }
}
