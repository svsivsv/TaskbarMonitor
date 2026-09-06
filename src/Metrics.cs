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
        public double? CpuTemperatureC { get; set; }
        public double? GpuTemperatureC { get; set; }

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
                return "—";
            }
            return FormatValue(option.Kind, compact);
        }

        public string FormatTemperature(MetricOption option)
        {
            return FormatTemperature(option, "°C");
        }

        public string FormatCompactTemperature(MetricOption option)
        {
            return FormatTemperature(option, "°");
        }

        private string FormatTemperature(MetricOption option, string suffix)
        {
            if (option == null || !option.ShowTemperature) return String.Empty;
            double? temperature = option.Kind == MetricKind.Cpu ? CpuTemperatureC :
                (option.Kind == MetricKind.Gpu ? GpuTemperatureC : null);
            if (!temperature.HasValue || temperature.Value < -20.0 || temperature.Value > 150.0)
                return String.Empty;
            return Math.Round(temperature.Value).ToString("0") + suffix;
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
        private readonly List<DateTime> timestamps = new List<DateTime>();
        private readonly IList<DateTime> timestampView;
        private int historySeconds = 60;
        private int intervalMs = 1000;
        public IList<DateTime> Timestamps { get { return timestampView; } }
        public int WindowSeconds { get { return historySeconds; } }
        public int IntervalMs { get { return intervalMs; } }

        public MetricHistory()
        {
            timestampView = timestamps.AsReadOnly();
            values = new Dictionary<MetricKind, List<double>>();
            foreach (MetricKind kind in Enum.GetValues(typeof(MetricKind)))
                values[kind] = new List<double>();
            diskValues = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
            maximumSamples = 600;
        }

        public void Configure(int historySeconds, int intervalMs)
        {
            this.historySeconds = Math.Max(10, Math.Min(600, historySeconds));
            this.intervalMs = Math.Max(200, intervalMs);
            // Keep the existing time window when the sampling interval changes.
            // The fastest supported rate (200 ms) needs at most 3000 points.
            maximumSamples = 3000;
            Trim();
        }

        public void Add(MetricSnapshot snapshot)
        {
            DateTime timestamp = snapshot.Timestamp;
            if (timestamp == DateTime.MinValue)
                timestamp = timestamps.Count == 0 ? DateTime.UtcNow : timestamps[timestamps.Count - 1].AddMilliseconds(intervalMs);
            if (timestamps.Count > 0 && timestamp < timestamps[timestamps.Count - 1])
            {
                timestamps.Clear();
                foreach (List<double> list in values.Values) list.Clear();
                diskValues.Clear();
            }
            if (snapshot.DiskPercents != null)
                foreach (string name in snapshot.DiskPercents.Keys)
                    if (!diskValues.ContainsKey(name))
                        diskValues[name] = Enumerable.Repeat(Double.NaN, timestamps.Count).ToList();
            timestamps.Add(timestamp);
            foreach (MetricKind kind in Enum.GetValues(typeof(MetricKind)))
            {
                List<double> list = values[kind];
                list.Add(snapshot.GetValue(kind));
            }
            foreach (KeyValuePair<string, List<double>> pair in diskValues)
            {
                double value;
                pair.Value.Add(snapshot.DiskPercents != null && snapshot.DiskPercents.TryGetValue(pair.Key, out value) ? value : Double.NaN);
            }
            Trim();
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
            int remove = Math.Max(0, timestamps.Count - maximumSamples);
            if (timestamps.Count > 0)
            {
                DateTime cutoff = timestamps[timestamps.Count - 1].AddSeconds(-historySeconds);
                while (remove < timestamps.Count && timestamps[remove] <= cutoff) remove++;
            }
            if (remove > 0)
            {
                timestamps.RemoveRange(0, remove);
                foreach (List<double> list in values.Values) list.RemoveRange(0, Math.Min(remove, list.Count));
                foreach (List<double> list in diskValues.Values) list.RemoveRange(0, Math.Min(remove, list.Count));
            }
            foreach (List<double> list in values.Values)
                if (list.Count > maximumSamples) list.RemoveRange(0, list.Count - maximumSamples);
            foreach (List<double> list in diskValues.Values)
                if (list.Count > maximumSamples) list.RemoveRange(0, list.Count - maximumSamples);
        }
    }

    internal sealed class TemperatureCache
    {
        internal const int RetentionSeconds = 10;
        private double? value;
        private long lastSuccess;

        public void Clear() { value = null; lastSuccess = 0; }

        public void Update(double? current, long now)
        {
            if (current.HasValue && !Double.IsNaN(current.Value) &&
                !Double.IsInfinity(current.Value) && current.Value >= -20 && current.Value <= 150)
            {
                value = current;
                lastSuccess = now;
            }
        }

        public double? Read(long now)
        {
            if (value.HasValue && (now - lastSuccess) / (double)Stopwatch.Frequency >= RetentionSeconds)
                Clear();
            return value;
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
        private PdhWildcardCounter cpuTemperatureCounter;
        private NvmlTemperatureReader gpuTemperatureReader;
        private long lastTemperatureSampleTimestamp;
        private readonly TemperatureCache cachedCpuTemperature = new TemperatureCache();
        private readonly TemperatureCache cachedGpuTemperature = new TemperatureCache();

        public MetricSampler()
        {
        }

        public MetricSnapshot Sample(AppSettings settings)
        {
            MetricSnapshot result = new MetricSnapshot();
            result.Timestamp = DateTime.UtcNow;
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
            SampleTemperatures(result, settings);
            return result;
        }

        private void SampleTemperatures(MetricSnapshot result, AppSettings settings)
        {
            MetricOption cpu = settings.Metrics.FirstOrDefault(delegate(MetricOption option) { return option.Kind == MetricKind.Cpu; });
            MetricOption gpu = settings.Metrics.FirstOrDefault(delegate(MetricOption option) { return option.Kind == MetricKind.Gpu; });
            bool cpuEnabled = cpu != null && cpu.Enabled && cpu.ShowTemperature;
            bool gpuEnabled = gpu != null && gpu.Enabled && gpu.ShowTemperature;
            if (!cpuEnabled)
            {
                ReleaseCpuTemperatureCounter();
                cachedCpuTemperature.Clear();
            }
            if (!gpuEnabled)
            {
                ReleaseGpuTemperatureReader();
                cachedGpuTemperature.Clear();
            }

            long now = Stopwatch.GetTimestamp();
            bool due = lastTemperatureSampleTimestamp == 0 ||
                (now - lastTemperatureSampleTimestamp) / (double)Stopwatch.Frequency >= 2.0;
            if (due && (cpuEnabled || gpuEnabled))
            {
                if (cpuEnabled)
                {
                    double? cpuTemperature = ReadCpuTemperature();
                    cachedCpuTemperature.Update(cpuTemperature, now);
                }
                if (gpuEnabled)
                {
                    double? gpuTemperature = ReadGpuTemperature();
                    cachedGpuTemperature.Update(gpuTemperature, now);
                }
                lastTemperatureSampleTimestamp = now;
            }
            result.CpuTemperatureC = cachedCpuTemperature.Read(now);
            result.GpuTemperatureC = cachedGpuTemperature.Read(now);
        }

        private double? ReadCpuTemperature()
        {
            if (cpuTemperatureCounter == null)
            {
                try
                {
                    cpuTemperatureCounter = new PdhWildcardCounter(
                        @"\Thermal Zone Information(*)\High Precision Temperature");
                }
                catch
                {
                    cpuTemperatureCounter = null;
                    return null;
                }
            }
            double? raw = cpuTemperatureCounter.ReadMaximumRaw();
            if (!raw.HasValue)
            {
                ReleaseCpuTemperatureCounter();
                return null;
            }
            double celsius = raw.Value > 200.0 ? raw.Value / 10.0 - 273.15 : raw.Value;
            return celsius >= -20.0 && celsius <= 150.0 ? (double?)celsius : null;
        }

        private double? ReadGpuTemperature()
        {
            if (gpuTemperatureReader == null) gpuTemperatureReader = new NvmlTemperatureReader();
            return gpuTemperatureReader.ReadMaximum();
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

        private void ReleaseCpuTemperatureCounter()
        {
            if (cpuTemperatureCounter == null) return;
            cpuTemperatureCounter.Dispose();
            cpuTemperatureCounter = null;
        }

        private void ReleaseGpuTemperatureReader()
        {
            if (gpuTemperatureReader == null) return;
            gpuTemperatureReader.Dispose();
            gpuTemperatureReader = null;
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
            ReleaseCpuTemperatureCounter();
            ReleaseGpuTemperatureReader();
        }
    }

    internal sealed class NvmlTemperatureReader : IDisposable
    {
        private bool initialized;
        private long lastInitializationAttemptTimestamp;

        public double? ReadMaximum()
        {
            if (!EnsureInitialized()) return null;
            try
            {
                uint count;
                if (NvmlNative.nvmlDeviceGetCount_v2(out count) != 0 || count == 0) return RetryLater();
                uint maximum = 0;
                bool found = false;
                for (uint index = 0; index < count; index++)
                {
                    IntPtr device;
                    uint temperature;
                    if (NvmlNative.nvmlDeviceGetHandleByIndex_v2(index, out device) != 0) continue;
                    if (NvmlNative.nvmlDeviceGetTemperature(device, 0, out temperature) != 0) continue;
                    if (temperature > 150) continue;
                    maximum = Math.Max(maximum, temperature);
                    found = true;
                }
                return found ? (double?)maximum : RetryLater();
            }
            catch
            {
                return RetryLater();
            }
        }

        private double? RetryLater()
        {
            Dispose();
            lastInitializationAttemptTimestamp = Stopwatch.GetTimestamp();
            return null;
        }

        private bool EnsureInitialized()
        {
            if (initialized) return true;
            long now = Stopwatch.GetTimestamp();
            if (lastInitializationAttemptTimestamp != 0 &&
                (now - lastInitializationAttemptTimestamp) / (double)Stopwatch.Frequency < 10.0)
                return false;
            lastInitializationAttemptTimestamp = now;
            try { initialized = NvmlNative.nvmlInit_v2() == 0; }
            catch { initialized = false; }
            return initialized;
        }

        public void Dispose()
        {
            if (!initialized) return;
            try { NvmlNative.nvmlShutdown(); }
            catch { }
            initialized = false;
        }
    }

    internal static class NvmlNative
    {
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlInit_v2();

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlShutdown();

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetCount_v2(out uint deviceCount);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetTemperature(IntPtr device, uint sensorType, out uint temperature);
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

        public double? ReadMaximumRaw()
        {
            if (query == IntPtr.Zero) return null;
            if (PdhNative.PdhCollectQueryData(query) != PdhNative.ERROR_SUCCESS) return null;
            uint bufferSize = 0;
            uint itemCount = 0;
            uint status = PdhNative.PdhGetFormattedCounterArray(counter, PdhNative.PDH_FMT_DOUBLE,
                ref bufferSize, ref itemCount, IntPtr.Zero);
            if (status != PdhNative.PDH_MORE_DATA || bufferSize == 0 || itemCount == 0) return null;
            IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
            try
            {
                status = PdhNative.PdhGetFormattedCounterArray(counter, PdhNative.PDH_FMT_DOUBLE,
                    ref bufferSize, ref itemCount, buffer);
                if (status != PdhNative.ERROR_SUCCESS) return null;
                int size = Marshal.SizeOf(typeof(PdhNative.PDH_FMT_COUNTERVALUE_ITEM));
                double maximum = Double.MinValue;
                bool found = false;
                for (uint index = 0; index < itemCount; index++)
                {
                    IntPtr itemPointer = new IntPtr(buffer.ToInt64() + index * size);
                    PdhNative.PDH_FMT_COUNTERVALUE_ITEM item = (PdhNative.PDH_FMT_COUNTERVALUE_ITEM)
                        Marshal.PtrToStructure(itemPointer, typeof(PdhNative.PDH_FMT_COUNTERVALUE_ITEM));
                    if (item.Value.CStatus > 1 || Double.IsNaN(item.Value.DoubleValue) ||
                        Double.IsInfinity(item.Value.DoubleValue)) continue;
                    maximum = Math.Max(maximum, item.Value.DoubleValue);
                    found = true;
                }
                return found ? (double?)maximum : null;
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
