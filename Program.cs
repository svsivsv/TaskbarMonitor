using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TaskbarMonitor
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            NativeMethods.EnableHighDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0 && String.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                RunSelfTest(args.Length > 1 ? args[1] : null);
                return;
            }
            if (args.Length > 0 && String.Equals(args[0], "--render-preview", StringComparison.OrdinalIgnoreCase))
            {
                RenderPreview(args.Length > 1 ? args[1] : Environment.CurrentDirectory);
                return;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, @"Local\TaskbarMonitor.SingleInstance.1", out createdNew))
            {
                if (!createdNew)
                {
                    IntPtr existing = NativeMethods.FindWindow(null, NativeMethods.MessageSinkCaption);
                    if (existing == IntPtr.Zero)
                        existing = NativeMethods.FindWindow(null, "Taskbar Monitor");
                    if (existing == IntPtr.Zero)
                    {
                        IntPtr taskbar = NativeMethods.GetPrimaryTaskbarHandle();
                        if (taskbar != IntPtr.Zero)
                            existing = NativeMethods.FindWindowEx(taskbar, IntPtr.Zero, null, "Taskbar Monitor");
                    }
                    if (existing != IntPtr.Zero)
                        NativeMethods.PostMessage(existing, NativeMethods.WM_APP_SHOW_SETTINGS, IntPtr.Zero, IntPtr.Zero);
                    else
                        NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_APP_SHOW_SETTINGS, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { ReportError(e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    Exception exception = e.ExceptionObject as Exception;
                    if (exception != null) ReportError(exception);
                };

                bool startup = args.Length > 0 && String.Equals(args[0], "--startup", StringComparison.OrdinalIgnoreCase);
                Application.Run(new AppHost(startup));
                GC.KeepAlive(mutex);
            }
        }

        private static void ReportError(Exception exception)
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.SettingsDirectory);
                File.AppendAllText(Path.Combine(SettingsStore.SettingsDirectory, "error.log"),
                    DateTime.Now.ToString("s") + Environment.NewLine + exception + Environment.NewLine + Environment.NewLine);
            }
            catch
            {
            }
            try
            {
                MessageBox.Show("예상하지 못한 오류가 발생했습니다. 오류 기록은 설정 폴더에 저장했습니다.\n\n" + exception.Message,
                    "Taskbar Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
            }
        }

        private static void RunSelfTest(string outputPath)
        {
            if (String.IsNullOrEmpty(outputPath))
                outputPath = Path.Combine(Path.GetTempPath(), "TaskbarMonitor-self-test.json");
            Dictionary<string, object> report = new Dictionary<string, object>();
            report["timestamp"] = DateTime.Now.ToString("o");
            report["machine"] = Environment.MachineName;
            report["framework"] = Environment.Version.ToString();
            try
            {
                AppSettings settings = AppSettings.CreateDefault();
                settings.SelectedDisks = AppSettings.GetAvailableDiskNames();
                using (MetricSampler sampler = new MetricSampler())
                {
                    sampler.Sample(settings);
                    Thread.Sleep(1200);
                    MetricSnapshot snapshot = sampler.Sample(settings);
                    report["cpuPercent"] = snapshot.CpuPercent;
                    report["memoryPercent"] = snapshot.MemoryPercent;
                    report["memoryUsedGb"] = snapshot.MemoryUsedGb;
                    report["memoryTotalGb"] = snapshot.MemoryTotalGb;
                    report["diskPercent"] = snapshot.DiskPercent;
                    report["diskPercents"] = snapshot.DiskPercents;
                    report["selectedDisks"] = settings.SelectedDisks;
                    report["networkDownloadBytes"] = snapshot.NetworkDownloadBytes;
                    report["networkUploadBytes"] = snapshot.NetworkUploadBytes;
                    report["gpuPercent"] = snapshot.GpuPercent;
                    IntPtr desktopWindow = NativeMethods.FindWindow("Progman", null);
                    bool desktopExcluded = !NativeMethods.IsWindowFullscreen(desktopWindow);
                    report["desktopExcludedFromFullscreen"] = desktopExcluded;
                    MetricOption memoryOption = settings.Metrics.First(delegate(MetricOption option) { return option.Kind == MetricKind.Memory; });
                    memoryOption.ValueFormat = "UsedTotalGb";
                    bool memoryFormatSupported = snapshot.FormatValue(memoryOption, true, null).Contains("/");
                    report["memoryFormatSupported"] = memoryFormatSupported;
                    report["success"] = snapshot.MemoryTotalGb > 0.0 && snapshot.CpuPercent >= 0.0 &&
                        snapshot.CpuPercent <= 100.0 && desktopExcluded && memoryFormatSupported &&
                        snapshot.DiskPercents != null && snapshot.DiskPercents.Count == settings.SelectedDisks.Count;
                }
            }
            catch (Exception ex)
            {
                report["success"] = false;
                report["error"] = ex.ToString();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
            File.WriteAllText(outputPath, new JavaScriptSerializer().Serialize(report));
            Environment.ExitCode = Convert.ToBoolean(report["success"]) ? 0 : 1;
        }

        private static void RenderPreview(string directory)
        {
            Directory.CreateDirectory(directory);
            AppSettings settings = AppSettings.CreateDefault();
            settings.MaxWidth = 900;
            settings.SelectedDisks = new List<string> { "C:", "E:" };
            settings.Metrics.First(delegate(MetricOption option) { return option.Kind == MetricKind.Memory; }).ValueFormat = "UsedTotalGb";
            MetricHistory history = new MetricHistory();
            history.Configure(60, 1000);
            for (int index = 0; index < 60; index++)
            {
                history.AddSynthetic(MetricKind.Cpu, 28 + Math.Sin(index / 4.0) * 18 + (index % 13 == 0 ? 35 : 0));
                history.AddSynthetic(MetricKind.Memory, 47 + Math.Sin(index / 12.0) * 3);
                history.AddSynthetic(MetricKind.Disk, Math.Max(0, Math.Sin(index / 3.5) * 30));
                history.AddSyntheticDisk("C:", Math.Max(0, Math.Sin(index / 3.5) * 30));
                history.AddSyntheticDisk("E:", Math.Max(0, Math.Cos(index / 4.2) * 24));
                history.AddSynthetic(MetricKind.Network, 80000 + Math.Abs(Math.Sin(index / 5.0)) * 850000);
                history.AddSynthetic(MetricKind.Gpu, 15 + Math.Abs(Math.Sin(index / 6.0)) * 42);
            }
            MetricSnapshot snapshot = new MetricSnapshot();
            snapshot.Timestamp = DateTime.Now;
            snapshot.CpuPercent = 37;
            snapshot.MemoryPercent = 48;
            snapshot.MemoryUsedGb = 15.4;
            snapshot.MemoryTotalGb = 31.8;
            snapshot.DiskPercent = 9;
            snapshot.DiskPercents["C:"] = 9;
            snapshot.DiskPercents["E:"] = 4;
            snapshot.NetworkDownloadBytes = 756000;
            snapshot.NetworkUploadBytes = 96000;
            snapshot.GpuPercent = 43;

            using (MetricBarControl bar = new MetricBarControl())
            {
                bar.Size = new Size(900, 48);
                bar.Configure(settings, snapshot, history);
                using (Bitmap image = new Bitmap(bar.Width, bar.Height))
                {
                    bar.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    image.Save(Path.Combine(directory, "bar-preview.png"));
                }
            }
            using (MetricBarControl pagedBar = new MetricBarControl())
            {
                settings.MaxWidth = 300;
                pagedBar.Size = new Size(300, 48);
                pagedBar.SetIntegratedStyle(true, Color.FromArgb(31, 31, 31));
                pagedBar.Configure(settings, snapshot, history);
                using (Bitmap image = new Bitmap(pagedBar.Width, pagedBar.Height))
                {
                    pagedBar.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    image.Save(Path.Combine(directory, "bar-preview-paged.png"));
                }
                pagedBar.TryNavigate(new Point(pagedBar.Width - 5, pagedBar.Height / 2));
                using (Bitmap image = new Bitmap(pagedBar.Width, pagedBar.Height))
                {
                    pagedBar.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    image.Save(Path.Combine(directory, "bar-preview-paged-next.png"));
                }
            }
            using (DetailGraphControl detail = new DetailGraphControl())
            {
                detail.Width = 560;
                detail.Configure(settings, snapshot, history);
                using (Bitmap image = new Bitmap(detail.Width, detail.Height))
                {
                    detail.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    image.Save(Path.Combine(directory, "detail-preview.png"));
                }
            }
        }
    }
}
