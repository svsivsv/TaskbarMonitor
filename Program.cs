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
        private const long MaximumErrorLogBytes = 512 * 1024;
        private static readonly object ErrorLogLock = new object();

        [STAThread]
        private static void Main(string[] args)
        {
            NativeMethods.EnableHighDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0 && (String.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(args[0], "--self-test-window", StringComparison.OrdinalIgnoreCase)))
            {
                RunSelfTest(args.Length > 1 ? args[1] : null,
                    String.Equals(args[0], "--self-test-window", StringComparison.OrdinalIgnoreCase));
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
                bool forceWidget = args.Length > 0 && String.Equals(args[0], "--show-widget", StringComparison.OrdinalIgnoreCase);
                Application.Run(new AppHost(startup, forceWidget));
                GC.KeepAlive(mutex);
            }
        }

        private static void ReportError(Exception exception)
        {
            LogError(exception);
            try
            {
                MessageBox.Show("예상하지 못한 오류가 발생했습니다. 오류 기록은 설정 폴더에 저장했습니다.\n\n" + exception.Message,
                    "Taskbar Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
            }
        }

        internal static void LogError(Exception exception)
        {
            if (exception == null) return;
            try
            {
                lock (ErrorLogLock)
                {
                    Directory.CreateDirectory(SettingsStore.SettingsDirectory);
                    string logPath = Path.Combine(SettingsStore.SettingsDirectory, "error.log");
                    if (File.Exists(logPath) && new FileInfo(logPath).Length >= MaximumErrorLogBytes)
                        File.WriteAllText(logPath, "이전 오류 기록은 512KB 제한으로 정리되었습니다." + Environment.NewLine);
                    File.AppendAllText(logPath,
                        DateTime.Now.ToString("s") + Environment.NewLine + exception + Environment.NewLine + Environment.NewLine);
                }
            }
            catch
            {
            }
        }

        private static void RunSelfTest(string outputPath, bool exerciseWindow)
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
                MetricOption defaultCpuOption = settings.Metrics.First(delegate(MetricOption option) { return option.Kind == MetricKind.Cpu; });
                MetricOption defaultGpuOption = settings.Metrics.First(delegate(MetricOption option) { return option.Kind == MetricKind.Gpu; });
                bool defaultResetPassed = settings.UpdateIntervalMs == 1000 && settings.HistorySeconds == 60 &&
                    settings.MaxWidth == 560 && settings.PopupWidth == 500 && settings.PopupHeight == 72 &&
                    settings.PositionMode == "Inside" && settings.HiddenMeasurementMode == "Stop" &&
                    !settings.OverflowPaging && settings.SettingsVersion == 7 && settings.Metrics.Count == 5 &&
                    defaultCpuOption.ShowTemperature && defaultGpuOption.ShowTemperature;
                report["defaultResetPassed"] = defaultResetPassed;
                AppSettings migratedSettings = AppSettings.CreateDefault();
                migratedSettings.SettingsVersion = 6;
                migratedSettings.Metrics.First(delegate(MetricOption option) { return option.Kind == MetricKind.Cpu; }).ShowTemperature = false;
                migratedSettings.Metrics.First(delegate(MetricOption option) { return option.Kind == MetricKind.Gpu; }).ShowTemperature = false;
                migratedSettings.EnsureDefaults();
                bool temperatureMigrationPassed = migratedSettings.SettingsVersion == 7 &&
                    migratedSettings.Metrics.First(delegate(MetricOption option) { return option.Kind == MetricKind.Cpu; }).ShowTemperature &&
                    migratedSettings.Metrics.First(delegate(MetricOption option) { return option.Kind == MetricKind.Gpu; }).ShowTemperature;
                report["temperatureMigrationPassed"] = temperatureMigrationPassed;
                bool temperatureRetentionPassed = MetricSampler.PreserveLastTemperature(48.0, null) == 48.0 &&
                    MetricSampler.PreserveLastTemperature(48.0, 51.0) == 51.0 &&
                    !MetricSampler.PreserveLastTemperature(null, null).HasValue;
                report["temperatureRetentionPassed"] = temperatureRetentionPassed;
                DateTime samplingNow = new DateTime(2026, 1, 1, 0, 0, 10, DateTimeKind.Utc);
                bool hiddenSamplingPolicyPassed =
                    AppHost.ShouldSampleMetrics(false, "Stop", samplingNow, samplingNow) &&
                    !AppHost.ShouldSampleMetrics(true, "Stop", DateTime.MinValue, samplingNow) &&
                    !AppHost.ShouldSampleMetrics(true, "Throttle", samplingNow.AddSeconds(-4), samplingNow) &&
                    AppHost.ShouldSampleMetrics(true, "Throttle", samplingNow.AddSeconds(-5), samplingNow) &&
                    AppHost.ShouldSampleMetrics(true, "Continue", samplingNow, samplingNow);
                report["hiddenSamplingModes"] = new string[] { "Stop", "Throttle", "Continue" };
                report["hiddenSamplingPolicyPassed"] = hiddenSamplingPolicyPassed;
                int seamlessPixel;
                int panelPixel;
                bool insideStyleRenderingPassed = ValidateInsideStyleRendering(out seamlessPixel, out panelPixel);
                report["insideSeamlessCornerArgb"] = seamlessPixel;
                report["insidePanelCornerArgb"] = panelPixel;
                report["insideStyleRenderingPassed"] = insideStyleRenderingPassed;
                Rectangle resizeWorkArea = new Rectangle(0, 0, 1920, 1040);
                Size normalResize = WidgetForm.CalculatePopupResizeSize(new Rectangle(100, 100, 500, 72), 120, 80, resizeWorkArea);
                Size minimumResize = WidgetForm.CalculatePopupResizeSize(new Rectangle(100, 100, 500, 72), -900, -900, resizeWorkArea);
                Size maximumResize = WidgetForm.CalculatePopupResizeSize(new Rectangle(1700, 900, 500, 72), 900, 900, resizeWorkArea);
                bool popupResizeCalculationPassed = normalResize == new Size(620, 152) &&
                    minimumResize == new Size(200, 48) && maximumResize == new Size(220, 140);
                report["popupResizeCalculationPassed"] = popupResizeCalculationPassed;
                bool widgetInputPolicyPassed = WidgetForm.ShouldAcceptWidgetInput(true, false) &&
                    !WidgetForm.ShouldAcceptWidgetInput(false, false) &&
                    !WidgetForm.ShouldAcceptWidgetInput(true, true) &&
                    !WidgetForm.ShouldAcceptWidgetInput(false, true);
                report["widgetInputPolicyPassed"] = widgetInputPolicyPassed;
                bool contextMenuAutoDismissConfigured = AppHost.ContextMenuAutoDismissMilliseconds == 1200;
                report["contextMenuAutoDismissConfigured"] = contextMenuAutoDismissConfigured;
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
                    report["cpuTemperatureC"] = snapshot.CpuTemperatureC;
                    report["gpuTemperatureC"] = snapshot.GpuTemperatureC;
                    bool temperatureReadingsPlausible = (!snapshot.CpuTemperatureC.HasValue ||
                            (snapshot.CpuTemperatureC.Value >= -20.0 && snapshot.CpuTemperatureC.Value <= 150.0)) &&
                        (!snapshot.GpuTemperatureC.HasValue ||
                            (snapshot.GpuTemperatureC.Value >= -20.0 && snapshot.GpuTemperatureC.Value <= 150.0));
                    MetricSnapshot temperatureFormattingSnapshot = new MetricSnapshot();
                    temperatureFormattingSnapshot.CpuTemperatureC = 57.4;
                    temperatureFormattingSnapshot.GpuTemperatureC = 50.6;
                    bool temperatureFormattingPassed = temperatureFormattingSnapshot.FormatTemperature(defaultCpuOption) == "57°" &&
                        temperatureFormattingSnapshot.FormatTemperature(defaultGpuOption) == "51°";
                    report["temperatureReadingsPlausible"] = temperatureReadingsPlausible;
                    report["temperatureFormattingPassed"] = temperatureFormattingPassed;
                    IntPtr desktopWindow = NativeMethods.FindWindow("Progman", null);
                    bool desktopExcluded = !NativeMethods.IsWindowFullscreen(desktopWindow);
                    report["desktopExcludedFromFullscreen"] = desktopExcluded;
                    IntPtr taskbarWindow = NativeMethods.GetPrimaryTaskbarHandle();
                    IntPtr widgetWindow = NativeMethods.FindWindow(null, "Taskbar Monitor");
                    AppSettings persistedSettings = SettingsStore.Load();
                    bool expectedClickThrough = !persistedSettings.WidgetInteractionEnabled;
                    int widgetExStyle = widgetWindow == IntPtr.Zero ? 0 :
                        NativeMethods.GetWindowLong(widgetWindow, NativeMethods.GWL_EXSTYLE);
                    IntPtr widgetChild = widgetWindow == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetTopWindow(widgetWindow);
                    int widgetChildExStyle = widgetChild == IntPtr.Zero ? 0 :
                        NativeMethods.GetWindowLong(widgetChild, NativeMethods.GWL_EXSTYLE);
                    bool widgetClickThroughStyle = (widgetExStyle & NativeMethods.WS_EX_TRANSPARENT) != 0;
                    bool widgetChildClickThroughStyle = (widgetChildExStyle & NativeMethods.WS_EX_TRANSPARENT) != 0;
                    bool clickThroughNativeStatePassed = !exerciseWindow ||
                        (widgetWindow != IntPtr.Zero && widgetClickThroughStyle == expectedClickThrough &&
                         (widgetChild == IntPtr.Zero || widgetChildClickThroughStyle == expectedClickThrough));
                    report["persistedWidgetInteractionEnabled"] = persistedSettings.WidgetInteractionEnabled;
                    bool runtimeInteractionEnabled = widgetWindow != IntPtr.Zero &&
                        NativeMethods.SendMessage(widgetWindow, NativeMethods.WM_APP_QUERY_INTERACTION_ENABLED,
                            IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero;
                    int runtimeTaskManagerLaunchCount = widgetWindow == IntPtr.Zero ? -1 :
                        NativeMethods.SendMessage(widgetWindow, NativeMethods.WM_APP_QUERY_TASK_MANAGER_LAUNCH_COUNT,
                            IntPtr.Zero, IntPtr.Zero).ToInt32();
                    bool disabledDoubleClickSuppressed = !exerciseWindow || widgetWindow != IntPtr.Zero;
                    if (widgetChild != IntPtr.Zero && !persistedSettings.WidgetInteractionEnabled)
                    {
                        int launchesBefore = runtimeTaskManagerLaunchCount;
                        NativeMethods.SendMessage(widgetChild, NativeMethods.WM_LBUTTONDBLCLK,
                            new IntPtr(1), new IntPtr((10 << 16) | 10));
                        Thread.Sleep(50);
                        int launchesAfter = NativeMethods.SendMessage(widgetWindow,
                            NativeMethods.WM_APP_QUERY_TASK_MANAGER_LAUNCH_COUNT,
                            IntPtr.Zero, IntPtr.Zero).ToInt32();
                        disabledDoubleClickSuppressed = launchesAfter == launchesBefore;
                        runtimeTaskManagerLaunchCount = launchesAfter;
                    }
                    bool runtimeInteractionMatchesSettings = !exerciseWindow ||
                        (widgetWindow != IntPtr.Zero && runtimeInteractionEnabled == persistedSettings.WidgetInteractionEnabled);
                    report["runtimeWidgetInteractionEnabled"] = runtimeInteractionEnabled;
                    report["runtimeTaskManagerLaunchCount"] = runtimeTaskManagerLaunchCount;
                    report["disabledDoubleClickSuppressed"] = disabledDoubleClickSuppressed;
                    report["runtimeInteractionMatchesSettings"] = runtimeInteractionMatchesSettings;
                    report["widgetClickThroughStyle"] = widgetClickThroughStyle;
                    report["widgetChildClickThroughStyle"] = widgetChildClickThroughStyle;
                    report["clickThroughNativeStatePassed"] = clickThroughNativeStatePassed;
                    IntPtr embeddedWidget = widgetWindow != IntPtr.Zero &&
                        NativeMethods.GetParent(widgetWindow) == taskbarWindow ? widgetWindow : IntPtr.Zero;
                    NativeMethods.RECT taskbarRect;
                    if (taskbarWindow != IntPtr.Zero && NativeMethods.GetWindowRect(taskbarWindow, out taskbarRect))
                    {
                        Rectangle taskbarBounds = taskbarRect.ToRectangle();
                        report["taskbarBounds"] = taskbarBounds.X + "," + taskbarBounds.Y + "," +
                            taskbarBounds.Width + "," + taskbarBounds.Height;
                        TaskbarFreeSlot slot = TaskbarLayoutProbe.GetFreeSlot(taskbarWindow, taskbarBounds, settings.TaskbarOffset);
                        report["taskbarFreeSlot"] = slot.Left + "," + slot.Right;
                    }
                    report["embeddedWidgetFound"] = embeddedWidget != IntPtr.Zero;
                    report["embeddedWidgetVisible"] = embeddedWidget != IntPtr.Zero && NativeMethods.IsWindowVisible(embeddedWidget);
                    report["embeddedWidgetTopChild"] = embeddedWidget != IntPtr.Zero &&
                        (NativeMethods.GetWindowLong(embeddedWidget, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOPMOST) != 0;
                    bool embeddedDockingPassed = !exerciseWindow;
                    NativeMethods.RECT embeddedRect;
                    if (embeddedWidget != IntPtr.Zero && NativeMethods.GetWindowRect(embeddedWidget, out embeddedRect))
                    {
                        Rectangle embeddedBounds = embeddedRect.ToRectangle();
                        NativeMethods.RECT ownerRect;
                        bool parentMatches = NativeMethods.GetParent(embeddedWidget) == taskbarWindow;
                        bool contained = NativeMethods.GetWindowRect(taskbarWindow, out ownerRect) &&
                            ownerRect.ToRectangle().Contains(embeddedBounds);
                        report["embeddedWidgetBounds"] = embeddedBounds.X + "," + embeddedBounds.Y + "," +
                            embeddedBounds.Width + "," + embeddedBounds.Height;
                        report["embeddedWidgetParentMatchesTaskbar"] = parentMatches;
                        report["embeddedWidgetContainedInTaskbar"] = contained;
                        report["embeddedWidgetStyle"] = NativeMethods.GetWindowLong(embeddedWidget, NativeMethods.GWL_STYLE);
                        bool actualEmbeddedDockingPassed = parentMatches && contained && NativeMethods.IsWindowVisible(embeddedWidget) &&
                            (NativeMethods.GetWindowLong(embeddedWidget, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOPMOST) != 0;
                        embeddedDockingPassed = !exerciseWindow || actualEmbeddedDockingPassed;
                    }
                    report["embeddedDockingPassed"] = embeddedDockingPassed;
                    IntPtr floatingWidget = embeddedWidget == IntPtr.Zero ? widgetWindow : IntPtr.Zero;
                    report["floatingWidgetFound"] = floatingWidget != IntPtr.Zero;
                    report["floatingWidgetVisible"] = floatingWidget != IntPtr.Zero && NativeMethods.IsWindowVisible(floatingWidget);
                    report["floatingWidgetTopMost"] = floatingWidget != IntPtr.Zero &&
                        (NativeMethods.GetWindowLong(floatingWidget, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOPMOST) != 0;
                    NativeMethods.RECT floatingRect;
                    bool windowChecksPassed = !exerciseWindow ||
                        (String.Equals(persistedSettings.PositionMode, "Inside", StringComparison.OrdinalIgnoreCase) && embeddedDockingPassed);
                    if (floatingWidget != IntPtr.Zero && NativeMethods.GetWindowRect(floatingWidget, out floatingRect))
                    {
                        Rectangle floatingBounds = floatingRect.ToRectangle();
                        report["floatingWidgetBounds"] = floatingBounds.X + "," + floatingBounds.Y + "," +
                            floatingBounds.Width + "," + floatingBounds.Height;
                        long packedPoint = ((long)(ushort)(floatingBounds.Bottom - 2) << 16) |
                            (ushort)(floatingBounds.Right - 2);
                        report["popupResizeGripHitTest"] = NativeMethods.SendMessage(floatingWidget,
                            NativeMethods.WM_NCHITTEST, IntPtr.Zero, new IntPtr(packedPoint)).ToInt32() == NativeMethods.HTBOTTOMRIGHT;
                        if (exerciseWindow && String.Equals(persistedSettings.PositionMode, "Popup", StringComparison.OrdinalIgnoreCase))
                        {
                            AppSettings settingsBeforeResizeTest = SettingsStore.Load();
                            try
                            {
                                int resizedWidth = Math.Min(1200, Math.Max(200, floatingBounds.Width + 37));
                                int resizedHeight = Math.Min(400, Math.Max(48, floatingBounds.Height + 23));
                                NativeMethods.SetWindowPos(floatingWidget, NativeMethods.HWND_TOP, floatingBounds.X, floatingBounds.Y,
                                    resizedWidth, resizedHeight, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                                NativeMethods.SendMessage(floatingWidget, NativeMethods.WM_EXITSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
                                Thread.Sleep(150);
                                AppSettings savedSettings = SettingsStore.Load();
                                report["resizedPopupWidthSaved"] = savedSettings.PopupWidth;
                                report["resizedPopupHeightSaved"] = savedSettings.PopupHeight;
                                windowChecksPassed = savedSettings.PopupWidth == resizedWidth && savedSettings.PopupHeight == resizedHeight;
                                report["windowResizePersistencePassed"] = windowChecksPassed;
                            }
                            finally
                            {
                                NativeMethods.SetWindowPos(floatingWidget, NativeMethods.HWND_TOP, floatingBounds.X, floatingBounds.Y,
                                    floatingBounds.Width, floatingBounds.Height,
                                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                                NativeMethods.SendMessage(floatingWidget, NativeMethods.WM_EXITSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
                                SettingsStore.Save(settingsBeforeResizeTest);
                            }
                        }
                        else if (exerciseWindow && !String.Equals(persistedSettings.PositionMode, "Inside", StringComparison.OrdinalIgnoreCase))
                            windowChecksPassed = NativeMethods.IsWindowVisible(floatingWidget);
                    }
                    report["windowChecksPassed"] = windowChecksPassed;
                    report["defaultFloatingZOrder"] = settings.FloatingZOrder;
                    report["defaultPopupShowOnStartup"] = settings.PopupShowOnStartup;
                    MetricOption memoryOption = settings.Metrics.First(delegate(MetricOption option) { return option.Kind == MetricKind.Memory; });
                    memoryOption.ValueFormat = "UsedTotalGb";
                    bool memoryFormatSupported = snapshot.FormatValue(memoryOption, true, null).Contains("/");
                    report["memoryFormatSupported"] = memoryFormatSupported;
                    AppSettings multiDiskSettings = settings.Clone();
                    multiDiskSettings.SelectedDisks = new List<string> { "C:", "E:" };
                    int multiDiskDisplayCount = DisplayMetricBuilder.Build(multiDiskSettings).Count;
                    int expectedMultiDiskDisplayCount = multiDiskSettings.Metrics.Count(delegate(MetricOption option)
                    {
                        return option.Enabled && option.Kind != MetricKind.Disk;
                    }) + 2;
                    report["multiDiskDisplayCount"] = multiDiskDisplayCount;
                    report["displayModes"] = new string[] { "Inside", "Above", "Popup" };
                    List<int> pagingFirstIndices;
                    List<int> pagingVisibleCounts;
                    bool pagingPassed = ValidatePaging(snapshot, out pagingFirstIndices, out pagingVisibleCounts);
                    report["pagingFirstIndices"] = pagingFirstIndices;
                    report["pagingVisibleCounts"] = pagingVisibleCounts;
                    report["pagingPassed"] = pagingPassed;
                    report["success"] = snapshot.MemoryTotalGb > 0.0 && snapshot.CpuPercent >= 0.0 &&
                        snapshot.CpuPercent <= 100.0 && desktopExcluded && memoryFormatSupported &&
                        snapshot.DiskPercents != null && snapshot.DiskPercents.Count == settings.SelectedDisks.Count &&
                        multiDiskDisplayCount == expectedMultiDiskDisplayCount && pagingPassed &&
                        defaultResetPassed && temperatureMigrationPassed && temperatureRetentionPassed &&
                        temperatureReadingsPlausible && temperatureFormattingPassed &&
                        hiddenSamplingPolicyPassed && insideStyleRenderingPassed && popupResizeCalculationPassed &&
                        widgetInputPolicyPassed && contextMenuAutoDismissConfigured &&
                        clickThroughNativeStatePassed && runtimeInteractionMatchesSettings && disabledDoubleClickSuppressed &&
                        String.Equals(settings.FloatingZOrder, "Normal", StringComparison.OrdinalIgnoreCase) &&
                        settings.PopupShowOnStartup && windowChecksPassed;
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

        private static bool ValidatePaging(MetricSnapshot snapshot, out List<int> firstIndices, out List<int> visibleCounts)
        {
            firstIndices = new List<int>();
            visibleCounts = new List<int>();
            AppSettings settings = AppSettings.CreateDefault();
            settings.PositionMode = "Popup";
            settings.MaxWidth = 300;
            settings.PopupWidth = 300;
            settings.PopupHeight = 72;
            settings.OverflowPaging = true;
            settings.SelectedDisks = new List<string> { "C:" };
            MetricHistory history = new MetricHistory();
            history.Configure(60, 1000);
            history.Add(snapshot);
            using (MetricBarControl bar = new MetricBarControl())
            {
                bar.Size = new Size(300, 72);
                bar.Configure(settings, snapshot, history);
                for (int page = 0; page < 3; page++)
                {
                    using (Bitmap image = new Bitmap(bar.Width, bar.Height))
                        bar.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    firstIndices.Add(bar.DiagnosticFirstVisibleIndex);
                    visibleCounts.Add(bar.DiagnosticVisibleItemCount);
                    if (page < 2 && !bar.TryNavigate(new Point(bar.Width - 5, bar.Height / 2))) return false;
                }
                return bar.DiagnosticPageCount == 3 &&
                    firstIndices.SequenceEqual(new int[] { 0, 2, 3 }) &&
                    visibleCounts.All(delegate(int count) { return count == 2; });
            }
        }

        private static bool ValidateInsideStyleRendering(out int seamlessPixel, out int panelPixel)
        {
            AppSettings settings = AppSettings.CreateDefault();
            foreach (MetricOption option in settings.Metrics) option.Enabled = false;
            MetricSnapshot snapshot = new MetricSnapshot();
            MetricHistory history = new MetricHistory();
            history.Configure(60, 1000);
            using (MetricBarControl bar = new MetricBarControl())
            {
                bar.Size = new Size(240, 28);
                bar.Configure(settings, snapshot, history);
                bar.SetIntegratedStyle(true, Color.FromArgb(31, 31, 31), true);
                using (Bitmap seamless = new Bitmap(bar.Width, bar.Height))
                {
                    bar.DrawToBitmap(seamless, new Rectangle(Point.Empty, seamless.Size));
                    seamlessPixel = seamless.GetPixel(0, 0).ToArgb();
                }
                bar.SetIntegratedStyle(true, Color.FromArgb(31, 31, 31), false);
                using (Bitmap panel = new Bitmap(bar.Width, bar.Height))
                {
                    bar.DrawToBitmap(panel, new Rectangle(Point.Empty, panel.Size));
                    panelPixel = panel.GetPixel(0, 0).ToArgb();
                }
            }
            return seamlessPixel == Color.FromArgb(31, 31, 31).ToArgb() && seamlessPixel != panelPixel;
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
            snapshot.CpuTemperatureC = 57;
            snapshot.GpuTemperatureC = 51;

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
            using (MetricBarControl popupBar = new MetricBarControl())
            {
                AppSettings popupSettings = settings.Clone();
                popupSettings.PositionMode = "Popup";
                popupSettings.PopupPinned = false;
                popupSettings.PopupWidth = 500;
                popupSettings.PopupHeight = 72;
                popupSettings.SelectedDisks = new List<string> { "C:" };
                popupBar.Size = new Size(popupSettings.PopupWidth, popupSettings.PopupHeight);
                popupBar.Configure(popupSettings, snapshot, history);
                using (Bitmap image = new Bitmap(popupBar.Width, popupBar.Height))
                {
                    popupBar.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    image.Save(Path.Combine(directory, "popup-preview.png"));
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
            using (MetricBarControl compactBar = new MetricBarControl())
            {
                AppSettings compactSettings = settings.Clone();
                compactSettings.PositionMode = "Inside";
                compactSettings.MaxWidth = 344;
                compactSettings.InsideItemWidth = 70;
                compactSettings.AutoFit = true;
                compactSettings.OverflowPaging = false;
                compactSettings.SelectedDisks = new List<string> { "C:" };
                compactSettings.Metrics.First(delegate(MetricOption option) { return option.Kind == MetricKind.Memory; }).ValueFormat = "Percent";
                compactBar.Size = new Size(344, 28);
                compactBar.SetIntegratedStyle(true, Color.FromArgb(31, 31, 31));
                compactBar.Configure(compactSettings, snapshot, history);
                using (Bitmap image = new Bitmap(compactBar.Width, compactBar.Height))
                {
                    compactBar.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    image.Save(Path.Combine(directory, "bar-preview-compact.png"));
                }
            }
        }
    }
}
