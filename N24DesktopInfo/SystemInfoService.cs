using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace N24DesktopInfo
{
    public record DiskInfoData(string Name, long TotalBytes, long FreeBytes, double UsedPercent);
    public record NetworkAdapterData(string Name, string IpAddress, string Speed);
    public record TrafficSample(double InBytesPerSec, double OutBytesPerSec);

    public class SystemInfoData
    {
        public string Hostname { get; set; } = "";
        public string OsName { get; set; } = "";
        public string OsBuild { get; set; } = "";
        public string CpuName { get; set; } = "";
        public int CpuCores { get; set; }
        public double CpuUsagePercent { get; set; }
        public ulong RamTotalBytes { get; set; }
        public ulong RamUsedBytes { get; set; }
        public double RamUsedPercent { get; set; }
        public List<DiskInfoData> Disks { get; set; } = new();
        public List<NetworkAdapterData> NetworkAdapters { get; set; } = new();
        public string ExternalIp { get; set; } = "...";
        public TimeSpan Uptime { get; set; }
        public double TrafficInBps { get; set; }
        public double TrafficOutBps { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    // ================================================================
    //  TRAFFIC MEASUREMENT STRATEGY
    //
    //  PROBLEM: On VMware VMs with vmxnet3, ALL standard counter
    //  APIs return identical low values (~10 KB/s) even when Task
    //  Manager and the Network Meter gadget show 400 MB/s.
    //
    //  Seven different APIs tested, all broken:
    //  1. .NET NetworkInterface.GetIPStatistics()
    //  2. GetIfEntry2 / MIB_IF_ROW2 (NDIS v2)
    //  3. GetIfTable / MIB_IFROW (NDIS v1)
    //  4. Win32_PerfRawData_Tcpip_NetworkInterface (WMI)
    //  5. Win32_PerfFormattedData_Tcpip_NetworkInterface (WMI)
    //  6. MSFT_NetAdapterStatisticsSettingData (CIM)
    //  7. MSNdis_StatisticsInfo (WMI root\WMI)
    //
    //  ROOT CAUSE HYPOTHESES:
    //  A) SR-IOV: Real traffic flows through VF adapter (invisible
    //     to standard queries if we filter by type)
    //  B) vmxnet3 NDIS counters genuinely broken
    //  C) Traffic bypasses NDIS (RDMA, VMDirectPath)
    //
    //  SOLUTION: Three independent probes, ALL adapter rows logged,
    //  NO type filtering, deduplication by counter values.
    //  Highest combined In+Out wins.
    // ================================================================

    public class SystemInfoService : IDisposable
    {
        private readonly AppConfig _config;
        private readonly HttpClient _httpClient;
        private string _cachedCpuName = "", _cachedOsName = "", _cachedOsBuild = "";
        private int _cachedCpuCores;
        private string _cachedExternalIp = "...";
        private DateTime _lastExternalIpFetch = DateTime.MinValue;
        private bool _staticInfoLoaded;

        // CPU
        private long _prevIdleTime, _prevKernelTime, _prevUserTime;
        private double _lastCpuPercent;

        // Traffic: 3 independent probes
        private readonly ProbeGetIfTable _probeIfTable;
        private readonly ProbePdh _probePdh;
        private readonly ProbeNdisWmi _probeNdis;
        private int _sampleCount;

        // Traffic history for chart
        private readonly TrafficSample[] _trafficHistory;
        private int _trafficHistoryIndex, _trafficHistoryCount;

        public SystemInfoService(AppConfig config)
        {
            _config = config;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            NativeMethods.GetSystemTimes(out _prevIdleTime, out _prevKernelTime, out _prevUserTime);
            _trafficHistory = new TrafficSample[Math.Max(10, config.Traffic.ChartSeconds)];

            _probeIfTable = new ProbeGetIfTable();
            _probePdh = new ProbePdh();
            _probeNdis = new ProbeNdisWmi();

            if (_config.Traffic.EnableDebugLog)
            {
                var sb = new StringBuilder();
                sb.AppendLine("===================================================");
                sb.AppendLine("  N24 Desktop Info - Traffic Probe Init");
                sb.AppendLine("===================================================");
                sb.AppendLine($"  GetIfTable v1:  {(_probeIfTable.Ok ? "OK" : "FAIL")}");
                sb.AppendLine($"  PDH counters:   {(_probePdh.Ok ? $"OK ({_probePdh.InstanceCount} instances)" : "FAIL")}");
                sb.AppendLine($"  NDIS WMI:       {(_probeNdis.Ok ? $"OK ({_probeNdis.AdapterName})" : "FAIL")}");
                sb.AppendLine("===================================================");
                WriteDebugLog(sb.ToString());
            }
        }

        public void GetTrafficHistory(List<TrafficSample> output)
        {
            output.Clear();
            if (_trafficHistoryCount == 0) return;
            int start = (_trafficHistoryIndex - _trafficHistoryCount + _trafficHistory.Length) % _trafficHistory.Length;
            for (int i = 0; i < _trafficHistoryCount; i++)
                output.Add(_trafficHistory[(start + i) % _trafficHistory.Length]);
        }

        public SystemInfoData Collect()
        {
            if (!_staticInfoLoaded) LoadStaticInfo();

            var data = new SystemInfoData
            {
                Hostname = Environment.MachineName,
                OsName = _cachedOsName, OsBuild = _cachedOsBuild,
                CpuName = _cachedCpuName, CpuCores = _cachedCpuCores,
                Timestamp = DateTime.Now
            };

            data.CpuUsagePercent = GetCpuUsage();
            CollectMemory(data);
            if (_config.Sections.ShowDisks) CollectDisks(data);
            if (_config.Sections.ShowNetwork) CollectNetworkAdapters(data);
            CollectTraffic(data);
            data.ExternalIp = _cachedExternalIp;
            if (_config.Sections.ShowExternalIP) RefreshExternalIpIfNeeded();
            data.Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            return data;
        }

        // ============================================================
        //  TRAFFIC: Sample all probes, take MAX
        // ============================================================

        private void CollectTraffic(SystemInfoData data)
        {
            bool verbose = _config.Traffic.EnableDebugLog && _sampleCount < 200;
            var log = verbose ? new StringBuilder() : null;

            log?.AppendLine($"═══ Sample #{_sampleCount} at {DateTime.Now:HH:mm:ss.fff} ═══");

            // --- Probe 1: GetIfTable v1 (ALL rows, no type filter) ---
            _probeIfTable.Sample(log, _sampleCount < 5);

            // --- Probe 2: PDH (PerformanceCounter) ---
            _probePdh.Sample(log);

            // --- Probe 3: NDIS WMI (root\WMI) ---
            _probeNdis.Sample(log);

            // --- Pick highest combined In+Out ---
            double bestIn = 0, bestOut = 0;
            string bestName = "none";

            void Consider(string name, double inBps, double outBps)
            {
                if (!double.IsFinite(inBps)) inBps = 0;
                if (!double.IsFinite(outBps)) outBps = 0;
                if (inBps + outBps > bestIn + bestOut)
                {
                    bestIn = inBps; bestOut = outBps; bestName = name;
                }
            }

            Consider("GetIfTable", _probeIfTable.InBps, _probeIfTable.OutBps);
            Consider("PDH", _probePdh.InBps, _probePdh.OutBps);
            Consider("NDIS-WMI", _probeNdis.InBps, _probeNdis.OutBps);

            data.TrafficInBps = double.IsFinite(bestIn) ? bestIn : 0;
            data.TrafficOutBps = double.IsFinite(bestOut) ? bestOut : 0;

            log?.AppendLine($"  ► WINNER: {bestName} → " +
                $"IN={FormatBps(bestIn)} OUT={FormatBps(bestOut)}");

            // Record to chart history
            _trafficHistory[_trafficHistoryIndex] = new TrafficSample(bestIn, bestOut);
            _trafficHistoryIndex = (_trafficHistoryIndex + 1) % _trafficHistory.Length;
            if (_trafficHistoryCount < _trafficHistory.Length) _trafficHistoryCount++;

            _sampleCount++;
            if (log != null) WriteDebugLog(log.ToString());
        }

        internal static string FormatBps(double bps)
        {
            if (bps >= 1024 * 1024) return $"{bps / 1024 / 1024:F1} MB/s";
            if (bps >= 1024) return $"{bps / 1024:F1} KB/s";
            return $"{bps:F0} B/s";
        }

        // ============================================================
        //  CPU
        // ============================================================

        private double GetCpuUsage()
        {
            if (!NativeMethods.GetSystemTimes(out long idle, out long kernel, out long user))
                return _lastCpuPercent;
            long dI = idle - _prevIdleTime, dK = kernel - _prevKernelTime, dU = user - _prevUserTime;
            _prevIdleTime = idle; _prevKernelTime = kernel; _prevUserTime = user;
            long total = dK + dU;
            if (total == 0) return _lastCpuPercent;
            _lastCpuPercent = (1.0 - (double)dI / total) * 100.0;
            return _lastCpuPercent;
        }

        // ============================================================
        //  STATIC INFO
        // ============================================================

        private void LoadStaticInfo()
        {
            _staticInfoLoaded = true;
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Caption, Version FROM Win32_OperatingSystem");
                foreach (ManagementObject o in s.Get())
                {
                    _cachedOsName = o["Caption"]?.ToString()?.Replace("Microsoft ", "") ?? "Windows";
                    _cachedOsBuild = o["Version"]?.ToString() ?? "";
                    o.Dispose(); break;
                }
            }
            catch { _cachedOsName = RuntimeInformation.OSDescription; }

            try
            {
                using var s = new ManagementObjectSearcher("SELECT Name, NumberOfCores FROM Win32_Processor");
                foreach (ManagementObject o in s.Get())
                {
                    _cachedCpuName = CleanCpuName(o["Name"]?.ToString() ?? "Unknown");
                    _cachedCpuCores = Convert.ToInt32(o["NumberOfCores"] ?? 0);
                    o.Dispose(); break;
                }
            }
            catch { _cachedCpuName = "Unknown"; _cachedCpuCores = Environment.ProcessorCount; }
        }

        private static string CleanCpuName(string raw) =>
            System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", " ")
                .Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "")
                .Replace("CPU ", "").Trim();

        // ============================================================
        //  MEMORY, DISK, NETWORK DISPLAY
        // ============================================================

        private static void CollectMemory(SystemInfoData data)
        {
            var m = new NativeMethods.MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>();
            if (NativeMethods.GlobalMemoryStatusEx(ref m))
            {
                data.RamTotalBytes = m.ullTotalPhys;
                data.RamUsedBytes = m.ullTotalPhys - m.ullAvailPhys;
                data.RamUsedPercent = m.dwMemoryLoad;
            }
        }

        private void CollectDisks(SystemInfoData data)
        {
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (!d.IsReady) continue;
                    if (_config.Disk.OnlyFixedDrives && d.DriveType != DriveType.Fixed) continue;
                    double pct = d.TotalSize > 0 ? (double)(d.TotalSize - d.TotalFreeSpace) / d.TotalSize * 100.0 : 0;
                    data.Disks.Add(new DiskInfoData(d.Name.TrimEnd('\\'), d.TotalSize, d.TotalFreeSpace, pct));
                }
            }
            catch { }
        }

        private void CollectNetworkAdapters(SystemInfoData data)
        {
            try
            {
                var ignore = _config.Network.IgnoreAdapters;
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (ignore.Any(f => nic.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                                        nic.Description.Contains(f, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var ip = nic.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                    if (ip == null) continue;
                    string speed = nic.Speed switch
                    {
                        >= 1_000_000_000 => $"{nic.Speed / 1_000_000_000} Gbit",
                        >= 1_000_000     => $"{nic.Speed / 1_000_000} Mbit",
                        > 0              => $"{nic.Speed / 1000} Kbit",
                        _ => ""
                    };
                    data.NetworkAdapters.Add(new NetworkAdapterData(nic.Name, ip.Address.ToString(), speed));
                }
            }
            catch { }
        }

        private void RefreshExternalIpIfNeeded()
        {
            if (DateTime.Now - _lastExternalIpFetch < TimeSpan.FromMinutes(_config.Refresh.ExternalIpIntervalMinutes))
                return;
            _lastExternalIpFetch = DateTime.Now;
            Task.Run(async () =>
            {
                try { _cachedExternalIp = (await _httpClient.GetStringAsync(_config.Network.ExternalIpUrl)).Trim(); }
                catch { _cachedExternalIp = "N/A"; }
            });
        }

        internal static void WriteDebugLog(string message)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "n24debug.log");
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
                if (File.Exists(path) && new FileInfo(path).Length > 300 * 1024)
                    File.WriteAllText(path, line);
                else
                    File.AppendAllText(path, line);
            }
            catch { }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _probePdh.Dispose();
        }
    }

    // ================================================================
    //  PROBE 1: GetIfTable v1 - ALL rows, no type filter
    //
    //  Previous bug: only logged type=6/71/53 with traffic > 0.
    //  This hid 20 of 24 rows. The real traffic adapter might have
    //  been among those hidden rows.
    //
    //  NOW: Log ALL rows. Deduplicate by counter value (not by name)
    //  to avoid 4x counting from NDIS filter layers.
    // ================================================================
    internal class ProbeGetIfTable
    {
        public bool Ok { get; private set; } = true;
        public double InBps { get; private set; }
        public double OutBps { get; private set; }

        // Track per-row previous values for accurate per-row deltas
        private readonly Dictionary<int, (uint prevIn, uint prevOut, DateTime prevTime)> _rowState = new();
        private bool _seeded;

        public void Sample(StringBuilder? log, bool fullDump)
        {
            try
            {
                int size = 0;
                int ret = NativeMethods.GetIfTable(null!, ref size, false);
                if (ret != NativeMethods.ERROR_INSUFFICIENT_BUFFER || size <= 0) return;

                byte[] buf = new byte[size];
                ret = NativeMethods.GetIfTable(buf, ref size, false);
                if (ret != 0) { log?.AppendLine($"  [GetIfTable] ERROR ret={ret}"); return; }

                int numEntries = BitConverter.ToInt32(buf, 0);
                log?.AppendLine($"  [GetIfTable v1] {numEntries} rows total:");

                var now = DateTime.UtcNow;
                double bestRowIn = 0, bestRowOut = 0;
                string bestRowName = "";

                // Deduplication set: avoid counting filter layers with same counters
                var seenCounters = new HashSet<(uint, uint)>();

                for (int i = 0; i < numEntries; i++)
                {
                    int off = 4 + i * NativeMethods.MIB_IFROW_SIZE;
                    if (off + NativeMethods.MIB_IFROW_SIZE > buf.Length) break;

                    uint ifIndex = BitConverter.ToUInt32(buf, off + 512);
                    uint ifType  = BitConverter.ToUInt32(buf, off + NativeMethods.IFROW_OFFSET_TYPE);
                    uint speed   = BitConverter.ToUInt32(buf, off + NativeMethods.IFROW_OFFSET_SPEED);
                    uint oper    = BitConverter.ToUInt32(buf, off + NativeMethods.IFROW_OFFSET_OPERSTATUS);
                    uint inOct   = BitConverter.ToUInt32(buf, off + NativeMethods.IFROW_OFFSET_IN_OCTETS);
                    uint outOct  = BitConverter.ToUInt32(buf, off + NativeMethods.IFROW_OFFSET_OUT_OCTETS);
                    uint dLen    = BitConverter.ToUInt32(buf, off + NativeMethods.IFROW_OFFSET_DESCRLEN);
                    string desc = "";
                    if (dLen > 0 && dLen <= 256)
                        desc = Encoding.ASCII.GetString(buf, off + NativeMethods.IFROW_OFFSET_DESCR,
                            (int)Math.Min(dLen, 256)).TrimEnd('\0');

                    // Log EVERY row on first 5 samples (full dump)
                    if (fullDump && log != null)
                    {
                        log.AppendLine($"    row[{i:D2}] idx={ifIndex} type={ifType} speed={speed} " +
                            $"oper={oper} In={inOct:N0} Out={outOct:N0} \"{desc}\"");
                    }

                    // Skip loopback (type 24) and rows with zero traffic
                    if (ifType == 24) continue;
                    if (inOct == 0 && outOct == 0) continue;

                    // Deduplicate: skip if identical counter values already seen
                    // (NDIS filter layers report same counters as base adapter)
                    if (!seenCounters.Add((inOct, outOct))) continue;

                    // Calculate per-row delta
                    if (_rowState.TryGetValue(i, out var prev))
                    {
                        double elapsed = (now - prev.prevTime).TotalSeconds;
                        if (elapsed > 0.05)
                        {
                            long dIn = (long)(inOct - prev.prevIn);
                            long dOut = (long)(outOct - prev.prevOut);
                            if (dIn < 0) dIn += 0x1_0000_0000L;
                            if (dOut < 0) dOut += 0x1_0000_0000L;

                            double rowInBps = dIn / elapsed;
                            double rowOutBps = dOut / elapsed;

                            if (!fullDump && log != null && (rowInBps > 1024 || rowOutBps > 1024))
                            {
                                log.AppendLine($"    [{desc}] type={ifType} " +
                                    $"Δ={dIn:N0}/{dOut:N0} → {SystemInfoService.FormatBps(rowInBps)}/{SystemInfoService.FormatBps(rowOutBps)}");
                            }

                            if (rowInBps + rowOutBps > bestRowIn + bestRowOut)
                            {
                                bestRowIn = rowInBps;
                                bestRowOut = rowOutBps;
                                bestRowName = desc;
                            }
                        }
                    }
                    _rowState[i] = (inOct, outOct, now);
                }

                if (!_seeded) { _seeded = true; InBps = 0; OutBps = 0; }
                else { InBps = bestRowIn; OutBps = bestRowOut; }

                if (log != null && !fullDump)
                    log.AppendLine($"  [GetIfTable] best=\"{bestRowName}\" " +
                        $"IN={SystemInfoService.FormatBps(InBps)} OUT={SystemInfoService.FormatBps(OutBps)}");
            }
            catch (Exception ex)
            {
                log?.AppendLine($"  [GetIfTable] EXCEPTION: {ex.Message}");
                Ok = false;
            }
        }

    }

    // ================================================================
    //  PROBE 2: PDH (System.Diagnostics.PerformanceCounter)
    //
    //  Uses the EXACT same counter category as Task Manager.
    //  Enumerates ALL instances of "Network Interface" category.
    //  Logs each instance's values.
    // ================================================================
    internal class ProbePdh : IDisposable
    {
        public bool Ok { get; private set; }
        public int InstanceCount { get; private set; }
        public double InBps { get; private set; }
        public double OutBps { get; private set; }

        private readonly List<(string name, PerformanceCounter cIn, PerformanceCounter cOut)> _counters = new();
        private bool _primed;

        public ProbePdh()
        {
            try
            {
                var cat = new PerformanceCounterCategory("Network Interface");
                var instances = cat.GetInstanceNames();
                InstanceCount = instances.Length;

                foreach (var inst in instances)
                {
                    try
                    {
                        var cIn = new PerformanceCounter("Network Interface", "Bytes Received/sec", inst, true);
                        var cOut = new PerformanceCounter("Network Interface", "Bytes Sent/sec", inst, true);
                        // Prime (first call always 0)
                        cIn.NextValue(); cOut.NextValue();
                        _counters.Add((inst, cIn, cOut));
                    }
                    catch { }
                }

                Ok = _counters.Count > 0;
            }
            catch { Ok = false; }
        }

        public void Sample(StringBuilder? log)
        {
            if (!Ok) return;
            try
            {
                double bestIn = 0, bestOut = 0;
                string bestName = "";

                foreach (var (name, cIn, cOut) in _counters)
                {
                    float vIn = cIn.NextValue();
                    float vOut = cOut.NextValue();
                    if (!float.IsFinite(vIn)) vIn = 0;
                    if (!float.IsFinite(vOut)) vOut = 0;

                    if (log != null && (vIn > 100 || vOut > 100))
                    {
                        log.AppendLine($"  [PDH] \"{name}\" " +
                            $"IN={SystemInfoService.FormatBps(vIn)} OUT={SystemInfoService.FormatBps(vOut)}");
                    }

                    if (vIn + vOut > bestIn + bestOut)
                    {
                        bestIn = vIn; bestOut = vOut; bestName = name;
                    }
                }

                if (!_primed) { _primed = true; InBps = 0; OutBps = 0; }
                else { InBps = bestIn; OutBps = bestOut; }

                if (log != null)
                    log.AppendLine($"  [PDH] best=\"{bestName}\" " +
                        $"IN={SystemInfoService.FormatBps(InBps)} OUT={SystemInfoService.FormatBps(OutBps)}");
            }
            catch (Exception ex)
            {
                log?.AppendLine($"  [PDH] EXCEPTION: {ex.Message}");
            }
        }

        public void Dispose()
        {
            foreach (var (_, cIn, cOut) in _counters)
            {
                cIn.Dispose(); cOut.Dispose();
            }
        }
    }

    // ================================================================
    //  PROBE 3: root\WMI MSNdis_StatisticsInfo
    //
    //  Queries NDIS OID_GEN_STATISTICS directly via WMI.
    //  Uses ifHCInOctets/ifHCOutOctets (64-bit, no wrap).
    //  Different code path than performance counters.
    // ================================================================
    internal class ProbeNdisWmi
    {
        public bool Ok { get; private set; }
        public string AdapterName { get; private set; } = "";
        public double InBps { get; private set; }
        public double OutBps { get; private set; }

        private ulong _prevIn, _prevOut;
        private DateTime _prevTime;
        private bool _seeded;

        public ProbeNdisWmi()
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\WMI");
                scope.Connect();
                using var s = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT InstanceName FROM MSNdis_StatisticsInfo"));

                foreach (ManagementObject o in s.Get())
                {
                    string name = o["InstanceName"]?.ToString() ?? "";
                    o.Dispose();
                    if (name.Length > 0 && !name.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
                    {
                        AdapterName = name;
                        // Prefer vmxnet3 or Ethernet
                        if (name.Contains("vmxnet3", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Ethernet", StringComparison.OrdinalIgnoreCase))
                            break;
                    }
                }

                Ok = AdapterName.Length > 0;
            }
            catch { Ok = false; }
        }

        public void Sample(StringBuilder? log)
        {
            if (!Ok) return;
            try
            {
                var opts = new ConnectionOptions { Timeout = TimeSpan.FromSeconds(2) };
                var scope = new ManagementScope(@"\\.\root\WMI", opts);
                scope.Connect();

                string esc = AdapterName.Replace("\\", "\\\\").Replace("'", "\\'");
                using var s = new ManagementObjectSearcher(scope,
                    new ObjectQuery(
                        $"SELECT ifHCInOctets, ifHCOutOctets FROM MSNdis_StatisticsInfo " +
                        $"WHERE InstanceName = '{esc}'"));

                foreach (ManagementObject o in s.Get())
                {
                    ulong inB = Convert.ToUInt64(o["ifHCInOctets"] ?? 0);
                    ulong outB = Convert.ToUInt64(o["ifHCOutOctets"] ?? 0);
                    o.Dispose();

                    var now = DateTime.UtcNow;
                    if (_seeded)
                    {
                        double elapsed = (now - _prevTime).TotalSeconds;
                        if (elapsed > 0.05)
                        {
                            InBps = (inB >= _prevIn ? inB - _prevIn : 0) / elapsed;
                            OutBps = (outB >= _prevOut ? outB - _prevOut : 0) / elapsed;
                        }
                    }
                    else _seeded = true;

                    _prevIn = inB; _prevOut = outB; _prevTime = now;

                    log?.AppendLine($"  [NDIS-WMI] \"{AdapterName}\" " +
                        $"total={inB:N0}/{outB:N0} " +
                        $"IN={SystemInfoService.FormatBps(InBps)} OUT={SystemInfoService.FormatBps(OutBps)}");
                    break;
                }
            }
            catch (Exception ex)
            {
                log?.AppendLine($"  [NDIS-WMI] EXCEPTION: {ex.Message}");
            }
        }
    }

}
