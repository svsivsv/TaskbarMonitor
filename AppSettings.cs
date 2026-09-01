using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace TaskbarMonitor
{
    public enum MetricKind
    {
        Cpu,
        Memory,
        Disk,
        Network,
        Gpu
    }

    public sealed class MetricOption
    {
        public MetricKind Kind { get; set; }
        public string Label { get; set; }
        public bool Enabled { get; set; }
        public bool ShowValue { get; set; }
        public bool ShowGraph { get; set; }
        public string GraphStyle { get; set; }
        public int ColorArgb { get; set; }
        public int Order { get; set; }
        public int Priority { get; set; }
        public bool AutoScale { get; set; }
        public double FixedMaximum { get; set; }

        public MetricOption Clone()
        {
            return (MetricOption)MemberwiseClone();
        }

        public string DisplayName
        {
            get
            {
                switch (Kind)
                {
                    case MetricKind.Cpu: return "CPU";
                    case MetricKind.Memory: return "메모리";
                    case MetricKind.Disk: return "디스크";
                    case MetricKind.Network: return "네트워크";
                    case MetricKind.Gpu: return "GPU";
                    default: return Kind.ToString();
                }
            }
        }

        public Color Color
        {
            get { return Color.FromArgb(ColorArgb); }
        }
    }

    public sealed class AppSettings
    {
        public List<MetricOption> Metrics { get; set; }
        public int UpdateIntervalMs { get; set; }
        public int HistorySeconds { get; set; }
        public int MaxWidth { get; set; }
        public int TaskbarOffset { get; set; }
        public int InsideItemWidth { get; set; }
        public int InsideHeight { get; set; }
        public string InsideStyle { get; set; }
        public int OpacityPercent { get; set; }
        public float FontSize { get; set; }
        public string PositionMode { get; set; }
        public string FullscreenMode { get; set; }
        public string CaptureMode { get; set; }
        public bool AutoFit { get; set; }
        public bool PauseWhenHidden { get; set; }
        public bool StartWithWindows { get; set; }
        public bool ShowSettingsOnManualLaunch { get; set; }
        public int BackgroundArgb { get; set; }
        public int ForegroundArgb { get; set; }
        public int BorderArgb { get; set; }

        public static AppSettings CreateDefault()
        {
            AppSettings value = new AppSettings();
            value.UpdateIntervalMs = 1000;
            value.HistorySeconds = 60;
            value.MaxWidth = 560;
            value.TaskbarOffset = 126;
            value.InsideItemWidth = 70;
            value.InsideHeight = 28;
            value.InsideStyle = "Seamless";
            value.OpacityPercent = 94;
            value.FontSize = 9.0f;
            value.PositionMode = "Inside";
            value.FullscreenMode = "Hide";
            value.CaptureMode = "Show";
            value.AutoFit = true;
            value.PauseWhenHidden = true;
            value.StartWithWindows = false;
            value.ShowSettingsOnManualLaunch = true;
            value.BackgroundArgb = Color.FromArgb(238, 28, 28, 28).ToArgb();
            value.ForegroundArgb = Color.FromArgb(245, 245, 245).ToArgb();
            value.BorderArgb = Color.FromArgb(80, 255, 255, 255).ToArgb();
            value.Metrics = CreateDefaultMetrics();
            return value;
        }

        private static List<MetricOption> CreateDefaultMetrics()
        {
            List<MetricOption> items = new List<MetricOption>();
            items.Add(NewMetric(MetricKind.Cpu, "CPU", Color.FromArgb(0, 183, 195), 0, true));
            items.Add(NewMetric(MetricKind.Memory, "RAM", Color.FromArgb(91, 155, 213), 1, true));
            items.Add(NewMetric(MetricKind.Disk, "DISK", Color.FromArgb(132, 192, 0), 2, true));
            items.Add(NewMetric(MetricKind.Network, "NET", Color.FromArgb(232, 62, 140), 3, true));
            items.Add(NewMetric(MetricKind.Gpu, "GPU", Color.FromArgb(184, 74, 216), 4, true));
            return items;
        }

        private static MetricOption NewMetric(MetricKind kind, string label, Color color, int order, bool enabled)
        {
            MetricOption item = new MetricOption();
            item.Kind = kind;
            item.Label = label;
            item.Enabled = enabled;
            item.ShowValue = true;
            item.ShowGraph = true;
            item.GraphStyle = "Line";
            item.ColorArgb = color.ToArgb();
            item.Order = order;
            item.Priority = order;
            item.AutoScale = kind == MetricKind.Network;
            item.FixedMaximum = kind == MetricKind.Network ? 1048576.0 : 100.0;
            return item;
        }

        public void EnsureDefaults()
        {
            AppSettings defaults = CreateDefault();
            if (Metrics == null) Metrics = new List<MetricOption>();
            foreach (MetricOption defaultMetric in defaults.Metrics)
            {
                if (!Metrics.Any(delegate(MetricOption m) { return m.Kind == defaultMetric.Kind; }))
                    Metrics.Add(defaultMetric);
            }
            if (UpdateIntervalMs < 200) UpdateIntervalMs = defaults.UpdateIntervalMs;
            if (HistorySeconds < 10) HistorySeconds = defaults.HistorySeconds;
            if (MaxWidth < 160) MaxWidth = defaults.MaxWidth;
            if (TaskbarOffset < 0) TaskbarOffset = defaults.TaskbarOffset;
            if (InsideItemWidth < 48 || InsideItemWidth > 180) InsideItemWidth = defaults.InsideItemWidth;
            if (InsideHeight < 20 || InsideHeight > 48) InsideHeight = defaults.InsideHeight;
            if (String.IsNullOrEmpty(InsideStyle)) InsideStyle = defaults.InsideStyle;
            if (OpacityPercent < 25 || OpacityPercent > 100) OpacityPercent = defaults.OpacityPercent;
            if (FontSize < 7.0f || FontSize > 18.0f) FontSize = defaults.FontSize;
            if (String.IsNullOrEmpty(PositionMode)) PositionMode = defaults.PositionMode;
            if (String.IsNullOrEmpty(FullscreenMode)) FullscreenMode = defaults.FullscreenMode;
            if (String.IsNullOrEmpty(CaptureMode)) CaptureMode = defaults.CaptureMode;
            if (BackgroundArgb == 0) BackgroundArgb = defaults.BackgroundArgb;
            if (ForegroundArgb == 0) ForegroundArgb = defaults.ForegroundArgb;
            if (BorderArgb == 0) BorderArgb = defaults.BorderArgb;
        }

        public AppSettings Clone()
        {
            AppSettings copy = (AppSettings)MemberwiseClone();
            copy.Metrics = Metrics.Select(delegate(MetricOption m) { return m.Clone(); }).ToList();
            return copy;
        }
    }

    public static class SettingsStore
    {
        private const string RunValueName = "TaskbarMonitor";

        public static string SettingsDirectory
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarMonitor");
            }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(SettingsDirectory, "settings.json"); }
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    AppSettings value = serializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                    if (value != null)
                    {
                        value.EnsureDefaults();
                        return value;
                    }
                }
            }
            catch
            {
            }
            return AppSettings.CreateDefault();
        }

        public static void Save(AppSettings settings)
        {
            Directory.CreateDirectory(SettingsDirectory);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            File.WriteAllText(SettingsPath, serializer.Serialize(settings));
        }

        public static void ApplyStartupSetting(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enabled)
                    key.SetValue(RunValueName, "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --startup");
                else
                    key.DeleteValue(RunValueName, false);
            }
        }
    }
}
