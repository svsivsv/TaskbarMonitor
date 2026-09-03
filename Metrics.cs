using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace TaskbarMonitor
{
    public sealed class MetricSnapshot
    {
        public MetricSnapshot()
        {
            DiskPercents = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        public DateTime Timestamp { get; set; }
        public double CpuPercent { get; set; }
        public double MemoryPercent { get; set; }
        public double MemoryUsedGb { get; set; }
        public double MemoryTotalGb { get; set; }
        public double DiskPercent { get; set; }
        public Dictionary<string, double> DiskPercents { get; set; }
        public double NetworkDownloadBytes { get; set; }
        public double NetworkUploadBytes { get; set; }
        public double GpuPercent { get; set; }

        public double GetValue(MetricKind kind)
        {
            switch (kind)
            {
                case MetricKind.Cpu: return CpuPercent;
                case MetricKind.Memory: return MemoryPercent;
                case MetricKind.Disk: return DiskPercent;
                case MetricKind.Network: return NetworkDownloadBytes + NetworkUploadBytes;
                case MetricKind.Gpu: return GpuPercent;
                default: return 0.0;
            }
        }

        public string FormatValue(MetricKind kind, bool compact)
        {
            switch (kind)
            {
                case MetricKind.Cpu:
                    return Math.Round(CpuPercent).ToString("0") + "%";
                case MetricKind.Memory:
                    return compact ? Math.Round(MemoryPercent).ToString("0") + "%" :
                        MemoryUsedGb.ToString("0.0") + "/" + MemoryTotalGb.ToString("0.0") + " GB";
                case MetricKind.Disk:
                    return Math.Round(DiskPercent).ToString("0") + "%";
                case MetricKind.Network:
                    if (compact) return FormatRateCompact(NetworkDownloadBytes + NetworkUploadBytes);
                    return "↓ " + FormatRate(NetworkDownloadBytes) + "  ↑ " + FormatRate(NetworkUploadBytes);
                case MetricKind.Gpu:
                    return Math.Round(GpuPercent).ToString("0") + "%";
                default:
                    return "-";
            }
        }

        public string FormatValue(MetricOption option, bool compact, string diskName)
        {
            if (option == null) return "-";
            if (option.Kind == MetricKind.Memory)
            {
                if (String.Equals(option.ValueFormat, "UsedGb", StringComparison.OrdinalIgnoreCase))
                    return MemoryUsedGb.ToString("0.0") + (compact ? "G" : " GB");
                if (String.Equals(option.ValueFormat, "UsedTotalGb", StringComparison.OrdinalIgnoreCase))
                    return compact ? Math.Round(MemoryUsedGb).ToString("0") + "/" + Math.Round(MemoryTotalGb).ToString("0") + "G" :
                        MemoryUsedGb.ToString("0.0") + "/" + MemoryTotalGb.ToString("0.0") + " GB";
            }
            if (option.Kind == MetricKind.Disk && !String.IsNullOrEmpty(diskName))
            {
                double value;
                if (DiskPercents != null && DiskPercents.TryGetValue(diskName, out value))
                    return Math.Round(value).ToString("0") + "%";
            }
            return FormatValue(option.Kind, compact);
        }

        public static string FormatRate(double bytesPerSecond)
        {
            if (bytesPerSecond >= 1073741824.0) return (bytesPerSecond / 1073741824.0).ToString("0.0") + " GB/s";
            if (bytesPerSecond >= 1048576.0) return (bytesPerSecond / 1048576.0).ToString("0.0") + " MB/s";
            if (bytesPerSecond >= 1024.0) return (bytesPerSecond / 1024.0).ToString("0") + " KB/s";
            return Math.Max(0.0, bytesPerSecond).ToString("0") + " B/s";
        }

        private static string FormatRateCompact(double bytesPerSecond)
        {
            if (bytesPerSecond >= 1073741824.0) return (bytesPerSecond / 1073741824.0).ToString("0.0") + "G";
            if (bytesPerSecond >= 1048576.0) return (bytesPerSecond / 1048576.0).ToString("0.0") + "M";
            if (bytesPerSecond >= 1024.0) return (bytesPerSecond / 1024.0).ToString("0") + "K";
            return Math.Max(0.0, bytesPerSecond).ToString("0");
        }
    }

    public sealed class MetricHistory
    {
        private static readonly IList<double> EmptyValues = new double[0];
        private readonly Dictionary<MetricKind, List<double>> values;
        private readonly Dictionary<string, List<double>> diskValues;
        private int maximumSamples;

        public MetricHistory()
        {
            values = new Dictionary<MetricKind, List<double>>();
            foreach (MetricKind kind in Enum.GetValues(typeof(MetricKind)))
                values[kind] = new List<double>();
            diskValues = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
            maximumSamples = 600;
        }

        public void Configure(int historySeconds, int intervalMs)
        {
            maximumSamples = Math.Max(10, Math.Min(2400, (int)Math.Ceiling(historySeconds * 1000.0 / Math.Max(200, intervalMs))));
            Trim();
        }

        public void Add(MetricSnapshot snapshot)
        {
            foreach (MetricKind kind in Enum.GetValues(typeof(MetricKind)))
            {
                List<double> list = values[kind];
                list.Add(snapshot.GetValue(kind));
                if (list.Count > maximumSamples)
                    list.RemoveRange(0, list.Count - maximumSamples);
            }
            if (snapshot.DiskPercents != null)
            {
                foreach (KeyValuePair<string, double> pair in snapshot.DiskPercents)
                {
                    List<double> list;
                    if (!diskValues.TryGetValue(pair.Key, out list))
                    {
                        list = new List<double>();
                        diskValues[pair.Key] = list;
                    }
                    list.Add(pair.Value);
                    if (list.Count > maximumSamples) list.RemoveRange(0, list.Count - maximumSamples);
                }
            }
        }

        public IList<double> GetValues(MetricKind kind)
        {
            return values[kind];
        }

        public IList<double> GetDiskValues(string diskName)
        {
            List<double> list;
            if (!String.IsNullOrEmpty(diskName) && diskValues.TryGetValue(diskName, out list))
                return list;
            return EmptyValues;
        }

        public void AddSynthetic(MetricKind kind, double value)
        {
            List<double> list = values[kind];
            list.Add(value);
            if (list.Count > maximumSamples) list.RemoveAt(0);
        }

        public void AddSyntheticDisk(string diskName, double value)
        {
            List<double> list;
            if (!diskValues.TryGetValue(diskName, out list))
            {
                list = new List<double>();
                diskValues[diskName] = list;
            }
            list.Add(value);
            if (list.Count > maximumSamples) list.RemoveAt(0);
        }

        private void Trim()
        {
            foreach (List<double> list in values.Values)
                if (list.Count > maximumSamples) list.RemoveRange(0, list.Count - maximumSamples);
            foreach (List<double> list in diskValues.Values)
                if (list.Count > maximumSamples) list.RemoveRange(0, list.Count - maximumSamples);
        }
    }

    public sealed class MetricSampler : IDisposable
    {
        private ulong previousIdle;
        private ulong previousKernel;
        private ulong previousUser;
        private long previousNetworkReceived;
        private long previousNetworkSent;
        private long previousNetworkTimestamp;
        private long lastNetworkAdapterRefreshTimestamp;
        private NetworkInterface[] networkAdapters;
        private bool hasCpuSample;
        private bool hasNetworkSample;
        private PdhSingleCounter diskCounter;
        private PdhWildcardCounter logicalDiskCounter;
        private PdhWildcardCounter gpuCounter;

        public MetricSampler()
        {
        }

        public MetricSnapshot Sample(AppSettings settings)
        {
            MetricSnapshot result = new MetricSnapshot();
            result.Timestamp = DateTime.Now;
            result.CpuPercent = SampleCpu();
            SampleMemory(result);
            SampleDisks(result, settings);
            SampleNetwork(result, IsEnabled(settings, MetricKind.Network));
            if (IsEnabled(settings, MetricKind.Gpu))
            {
                EnsureGpuCounter();
                result.GpuPercent = gpuCounter == null ? 0.0 : gpuCounter.ReadGpuMaximum();
            }
            else
                ReleaseGpuCounter();
            return result;
        }

        private void SampleDisks(MetricSnapshot result, AppSettings settings)
        {
            if (!IsEnabled(settings, MetricKind.Disk))
            {
                ReleaseDiskCounters();
                return;
            }
            EnsureDiskCounters();
            List<string> selected = settings.SelectedDisks == null ? new List<string>() :
                settings.SelectedDisks.Where(delegate(string name) { return !String.IsNullOrWhiteSpace(name); })
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Dictionary<string, double> current = logicalDiskCounter == null ?
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) : logicalDiskCounter.ReadValues();
            foreach (string diskName in selected)
            {
                double value;
                if (current.TryGetValue(diskName, out value)) result.DiskPercents[diskName] = value;
            }
            if (result.DiskPercents.Count > 0)
                result.DiskPercent = result.DiskPercents.Values.Max();
            else if (diskCounter != null)
                result.DiskPercent = diskCounter.Read();
        }

        private static bool IsEnabled(AppSettings settings, MetricKind kind)
        {
            MetricOption option = settings.Metrics.FirstOrDefault(delegate(MetricOption m) { return m.Kind == kind; });
            return option != null && option.Enabled;
        }

        private double SampleCpu()
        {
            NativeMethods.FILETIME idle;
            NativeMethods.FILETIME kernel;
            NativeMethods.FILETIME user;
            if (!NativeMethods.GetSystemTimes(out idle, out kernel, out user)) return 0.0;
            ulong idleNow = idle.ToUInt64();
            ulong kernelNow = kernel.ToUInt64();
            ulong userNow = user.ToUInt64();
            if (!hasCpuSample)
            {
                previousIdle = idleNow;
                previousKernel = kernelNow;
                previousUser = userNow;
                hasCpuSample = true;
                return 0.0;
            }
            ulong idleDiff = idleNow - previousIdle;
            ulong kernelDiff = kernelNow - previousKernel;
            ulong userDiff = userNow - previousUser;
            ulong total = kernelDiff + userDiff;
            previousIdle = idleNow;
            previousKernel = kernelNow;
            previousUser = userNow;
            if (total == 0) return 0.0;
            return Clamp((total - Math.Min(total, idleDiff)) * 100.0 / total);
        }

        private static void SampleMemory(MetricSnapshot result)
        {
            NativeMethods.MEMORYSTATUSEX memory = new NativeMethods.MEMORYSTATUSEX();
            if (!NativeMethods.GlobalMemoryStatusEx(memory)) return;
            result.MemoryPercent = memory.dwMemoryLoad;
            result.MemoryTotalGb = memory.ullTotalPhys / 1073741824.0;
            result.MemoryUsedGb = (memory.ullTotalPhys - memory.ullAvailPhys) / 1073741824.0;
        }

        private void SampleNetwork(MetricSnapshot result, bool enabled)
        {
            if (!enabled)
            {
                hasNetworkSample = false;
                return;
            }
            long received = 0;
            long sent = 0;
            try
            {
                foreach (NetworkInterface adapter in GetNetworkAdapters())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                    IPv4InterfaceStatistics statistics = adapter.GetIPv4Statistics();
                    received += statistics.BytesReceived;
                    sent += statistics.BytesSent;
                }
            }
            catch
            {
                return;
            }
            long now = Stopwatch.GetTimestamp();
            if (hasNetworkSample)
            {
                double seconds = Math.Max(0.001, (now - previousNetworkTimestamp) / (double)Stopwatch.Frequency);
                if (received >= previousNetworkReceived) result.NetworkDownloadBytes = (received - previousNetworkReceived) / seconds;
                if (sent >= previousNetworkSent) result.NetworkUploadBytes = (sent - previousNetworkSent) / seconds;
            }
            previousNetworkReceived = received;
            previousNetworkSent = sent;
            previousNetworkTimestamp = now;
            hasNetworkSample = true;
        }

        private NetworkInterface[] GetNetworkAdapters()
        {
            long now = Stopwatch.GetTimestamp();
            bool refresh = networkAdapters == null || lastNetworkAdapterRefreshTimestamp == 0 ||
                (now - lastNetworkAdapterRefreshTimestamp) / (double)Stopwatch.Frequency >= 10.0;
            if (refresh)
            {
                networkAdapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(delegate(NetworkInterface adapter)
                    {
                        return adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            adapter.NetworkInterfaceType != NetworkInterfaceType.Tunnel;
                    }).ToArray();
                lastNetworkAdapterRefreshTimestamp = now;
            }
            return networkAdapters;
        }

        private void EnsureDiskCounters()
        {
            if (diskCounter == null)
            {
                try { diskCounter = new PdhSingleCounter(@"\PhysicalDisk(_Total)\% Disk Time"); }
                catch { diskCounter = null; }
            }
            if (logicalDiskCounter == null)
            {
                try { logicalDiskCounter = new PdhWildcardCounter(@"\LogicalDisk(*)\% Disk Time"); }
                catch { logicalDiskCounter = null; }
            }
        }

        private void EnsureGpuCounter()
        {
            if (gpuCounter != null) return;
            try { gpuCounter = new PdhWildcardCounter(@"\GPU Engine(*)\Utilization Percentage"); }
            catch { gpuCounter = null; }
        }

        private void ReleaseDiskCounters()
        {
            if (diskCounter != null)
            {
                diskCounter.Dispose();
                diskCounter = null;
            }
            if (logicalDiskCounter != null)
            {
                logicalDiskCounter.Dispose();
                logicalDiskCounter = null;
            }
        }

        private void ReleaseGpuCounter()
        {
            if (gpuCounter == null) return;
            gpuCounter.Dispose();
            gpuCounter = null;
        }

        private static double Clamp(double value)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value)) return 0.0;
            return Math.Max(0.0, Math.Min(100.0, value));
        }

        public void Dispose()
        {
            ReleaseDiskCounters();
            ReleaseGpuCounter();
        }
    }

    internal static class PdhNative
    {
        public const uint PDH_FMT_DOUBLE = 0x00000200;
        public const uint ERROR_SUCCESS = 0;
        public const uint PDH_MORE_DATA = 0x800007D2;

        [StructLayout(LayoutKind.Sequential)]
        public struct PDH_FMT_COUNTERVALUE
        {
            public uint CStatus;
            public double DoubleValue;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PDH_FMT_COUNTERVALUE_ITEM
        {
            public IntPtr Name;
            public PDH_FMT_COUNTERVALUE Value;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        public static extern uint PdhOpenQuery(string source, IntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        public static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll")]
        public static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll")]
        public static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        public static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr itemBuffer);

        [DllImport("pdh.dll")]
        public static extern uint PdhCloseQuery(IntPtr query);
    }

    internal sealed class PdhSingleCounter : IDisposable
    {
        private IntPtr query;
        private IntPtr counter;

        public PdhSingleCounter(string path)
        {
            if (PdhNative.PdhOpenQuery(null, IntPtr.Zero, out query) != PdhNative.ERROR_SUCCESS)
                throw new InvalidOperationException("PDH query failed.");
            if (PdhNative.PdhAddEnglishCounter(query, path, IntPtr.Zero, out counter) != PdhNative.ERROR_SUCCESS)
            {
                PdhNative.PdhCloseQuery(query);
                query = IntPtr.Zero;
                throw new InvalidOperationException("PDH counter failed: " + path);
            }
            PdhNative.PdhCollectQueryData(query);
        }

        public double Read()
        {
            if (query == IntPtr.Zero) return 0.0;
            if (PdhNative.PdhCollectQueryData(query) != PdhNative.ERROR_SUCCESS) return 0.0;
            uint type;
            PdhNative.PDH_FMT_COUNTERVALUE value;
            if (PdhNative.PdhGetFormattedCounterValue(counter, PdhNative.PDH_FMT_DOUBLE, out type, out value) != PdhNative.ERROR_SUCCESS)
                return 0.0;
            if (value.CStatus > 1 || Double.IsNaN(value.DoubleValue)) return 0.0;
            return Math.Max(0.0, Math.Min(100.0, value.DoubleValue));
        }

        public void Dispose()
        {
            if (query != IntPtr.Zero)
            {
                PdhNative.PdhCloseQuery(query);
                query = IntPtr.Zero;
            }
        }
    }

    internal sealed class PdhWildcardCounter : IDisposable
    {
        private IntPtr query;
        private IntPtr counter;

        public PdhWildcardCounter(string path)
        {
            if (PdhNative.PdhOpenQuery(null, IntPtr.Zero, out query) != PdhNative.ERROR_SUCCESS)
                throw new InvalidOperationException("PDH query failed.");
            if (PdhNative.PdhAddEnglishCounter(query, path, IntPtr.Zero, out counter) != PdhNative.ERROR_SUCCESS)
            {
                PdhNative.PdhCloseQuery(query);
                query = IntPtr.Zero;
                throw new InvalidOperationException("PDH wildcard counter failed: " + path);
            }
            PdhNative.PdhCollectQueryData(query);
        }

        public double ReadGpuMaximum()
        {
            if (query == IntPtr.Zero) return 0.0;
            if (PdhNative.PdhCollectQueryData(query) != PdhNative.ERROR_SUCCESS) return 0.0;
            uint bufferSize = 0;
            uint itemCount = 0;
            uint status = PdhNative.PdhGetFormattedCounterArray(counter, PdhNative.PDH_FMT_DOUBLE, ref bufferSize, ref itemCount, IntPtr.Zero);
            if (status != PdhNative.PDH_MORE_DATA || bufferSize == 0 || itemCount == 0) return 0.0;
            IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
            try
            {
                status = PdhNative.PdhGetFormattedCounterArray(counter, PdhNative.PDH_FMT_DOUBLE, ref bufferSize, ref itemCount, buffer);
                if (status != PdhNative.ERROR_SUCCESS) return 0.0;
                int size = Marshal.SizeOf(typeof(PdhNative.PDH_FMT_COUNTERVALUE_ITEM));
                Dictionary<string, double> engines = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                double fallbackMaximum = 0.0;
                for (uint index = 0; index < itemCount; index++)
                {
                    IntPtr itemPointer = new IntPtr(buffer.ToInt64() + index * size);
                    PdhNative.PDH_FMT_COUNTERVALUE_ITEM item = (PdhNative.PDH_FMT_COUNTERVALUE_ITEM)Marshal.PtrToStructure(itemPointer, typeof(PdhNative.PDH_FMT_COUNTERVALUE_ITEM));
                    if (item.Value.CStatus > 1 || Double.IsNaN(item.Value.DoubleValue)) continue;
                    double value = Math.Max(0.0, item.Value.DoubleValue);
                    fallbackMaximum = Math.Max(fallbackMaximum, value);
                    string name = Marshal.PtrToStringUni(item.Name) ?? String.Empty;
                    string key = GetEngineKey(name);
                    double current;
                    engines.TryGetValue(key, out current);
                    engines[key] = current + value;
                }
                double maximum = engines.Count == 0 ? fallbackMaximum : engines.Values.Max();
                return Math.Max(0.0, Math.Min(100.0, maximum));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public Dictionary<string, double> ReadValues()
        {
            Dictionary<string, double> result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (query == IntPtr.Zero) return result;
            if (PdhNative.PdhCollectQueryData(query) != PdhNative.ERROR_SUCCESS) return result;
            uint bufferSize = 0;
            uint itemCount = 0;
            uint status = PdhNative.PdhGetFormattedCounterArray(counter, PdhNative.PDH_FMT_DOUBLE, ref bufferSize, ref itemCount, IntPtr.Zero);
            if (status != PdhNative.PDH_MORE_DATA || bufferSize == 0 || itemCount == 0) return result;
            IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
            try
            {
                status = PdhNative.PdhGetFormattedCounterArray(counter, PdhNative.PDH_FMT_DOUBLE, ref bufferSize, ref itemCount, buffer);
                if (status != PdhNative.ERROR_SUCCESS) return result;
                int size = Marshal.SizeOf(typeof(PdhNative.PDH_FMT_COUNTERVALUE_ITEM));
                for (uint index = 0; index < itemCount; index++)
                {
                    IntPtr itemPointer = new IntPtr(buffer.ToInt64() + index * size);
                    PdhNative.PDH_FMT_COUNTERVALUE_ITEM item = (PdhNative.PDH_FMT_COUNTERVALUE_ITEM)Marshal.PtrToStructure(itemPointer, typeof(PdhNative.PDH_FMT_COUNTERVALUE_ITEM));
                    if (item.Value.CStatus > 1 || Double.IsNaN(item.Value.DoubleValue)) continue;
                    string name = Marshal.PtrToStringUni(item.Name) ?? String.Empty;
                    if (String.IsNullOrEmpty(name) || name == "_Total") continue;
                    result[name] = Math.Max(0.0, Math.Min(100.0, item.Value.DoubleValue));
                }
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string GetEngineKey(string instanceName)
        {
            string lower = instanceName.ToLowerInvariant();
            int start = lower.IndexOf("luid_", StringComparison.Ordinal);
            int type = lower.IndexOf("_engtype_", StringComparison.Ordinal);
            if (start >= 0 && type > start) return lower.Substring(start, type - start);
            return lower;
        }

        public void Dispose()
        {
            if (query != IntPtr.Zero)
            {
                PdhNative.PdhCloseQuery(query);
                query = IntPtr.Zero;
            }
        }
    }
}
