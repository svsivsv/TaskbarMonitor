using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TaskbarMonitor
{
    // Hardware-independent tests: no host startup, user settings or live-window input.
    internal static class RegressionTests
    {
        public static Dictionary<string, object> Run()
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            try
            {
                result["temperatureExpiresAndRecovers"] = ValidateTemperatureCache();
                result["errorLogsExcludePrivateText"] = ValidateErrorLogPrivacy();
                result["startupTaskPolicy"] = ValidateStartupTaskPolicy();
                result["productionTextLayout"] = ValidateTextLayout();
                result["textPixelClipping"] = ValidateTextPixels();
                result["dataRefreshPreservesControl"] = ValidateDataRefresh();
                result["history600SecondsAt200ms"] = ValidateHistory();
                result["historyUsesElapsedTime"] = ValidateTimedHistory();
                result["missingDiskDoesNotBorrowValue"] = ValidateMissingDisk();
                result["largeFontsFitHeight"] = ValidateFontHeight();
                result["graphsHandleMissingSamples"] = ValidateMissingGraphSamples();
                result["graphsKeepWholeWindowAndGaps"] = ValidateGraphWindow();
                result["unreadableSettingsPreserved"] = ValidateSettingsRecovery();
                result["taskbarDoesNotInventSpace"] = ValidateSlots();
                result["startupSlotSettlesBeforeDisplay"] = ValidateSlotStabilization();
                result["requestedDefaults"] = ValidateRequestedDefaults();
                result["invalidSettingsNormalized"] = ValidateInvalidSettings();
                result["hiddenMeasurementModes"] = ValidateHiddenMeasurementModes();
                result["interactionGate"] = WidgetForm.ShouldAcceptWidgetInput(true, false) &&
                    !WidgetForm.ShouldAcceptWidgetInput(false, false) && !WidgetForm.ShouldAcceptWidgetInput(true, true);
                result["pagingAndDisabledInput"] = ValidatePaging();
                result["popupResizeLimits"] = ValidatePopupResize();
                result["settingsFooterFitsMinimumWidth"] = ValidateSettingsFooter();
                result["settingsDoNotOverrideShellVisibility"] =
                    WidgetForm.ShouldHideForEnvironment(true, true, true) &&
                    WidgetForm.ShouldHideForEnvironment(false, true, true) &&
                    !WidgetForm.ShouldHideForEnvironment(true, false, true) &&
                    !WidgetForm.ShouldHideForEnvironment(true, true, false);
                result["success"] = result.Values.All(delegate(object value) { return value is bool && (bool)value; });
            }
            catch (Exception ex) { result["success"] = false; result["error"] = ex.ToString(); }
            return result;
        }

        internal static bool ValidateTemperatureCache()
        {
            TemperatureCache cache = new TemperatureCache();
            long second = Stopwatch.Frequency;
            if (cache.Read(second).HasValue) return false;
            cache.Update(58, second);
            cache.Update(null, 5 * second);
            if (cache.Read(10 * second) != 58) return false;
            if (cache.Read(11 * second).HasValue) return false;
            cache.Update(Double.NaN, 12 * second);
            cache.Update(200, 12 * second);
            if (cache.Read(12 * second).HasValue) return false;
            cache.Update(61, 13 * second);
            if (cache.Read(13 * second) != 61) return false;
            cache.Clear();
            return !cache.Read(14 * second).HasValue;
        }

        private static bool ValidateErrorLogPrivacy()
        {
            const string privateText = "PRIVATE_SENTINEL";
            Exception captured;
            try
            {
                var inner = new System.IO.FileNotFoundException(
                    "token=" + privateText + " https://example.invalid/" + privateText,
                    @"C:\Users\" + privateText + @"\개인\settings.json");
                inner.Data[privateText] = @"\\server\" + privateText;
                throw new InvalidOperationException("message " + privateText, inner);
            }
            catch (Exception ex) { captured = ex; }
            string record = Program.FormatErrorRecord(captured);
            return !record.Contains(privateText) && !record.Contains("settings.json") &&
                !record.Contains("C:\\") && !record.Contains("https://") &&
                record.Contains("InvalidOperationException") && record.Contains("FileNotFoundException") &&
                record.Contains("Code: 0x") && record.Contains("RegressionTests.ValidateErrorLogPrivacy") &&
                Program.FormatErrorRecord(null) == String.Empty;
        }

        private static bool ValidateStartupTaskPolicy()
        {
            var xml = new System.Xml.XmlDocument();
            const string executable = @"E:\Apps & Tools\Monitor\TaskbarMonitor.exe";
            xml.LoadXml(StartupRegistration.BuildTaskXml(executable, "S-1-5-21-1-2-3-1001"));
            var ns = new System.Xml.XmlNamespaceManager(xml.NameTable);
            ns.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");
            Func<string, string> value = delegate(string path) { return xml.SelectSingleNode("/t:Task/" + path, ns).InnerText; };
            return value("t:Actions/t:Exec/t:Command") == executable &&
                value("t:Actions/t:Exec/t:Arguments") == "--startup" &&
                value("t:Triggers/t:LogonTrigger/t:Delay") == "PT20S" &&
                value("t:Principals/t:Principal/t:LogonType") == "InteractiveToken" &&
                value("t:Principals/t:Principal/t:RunLevel") == "LeastPrivilege" &&
                value("t:Settings/t:RestartOnFailure/t:Count") == "3" &&
                value("t:Settings/t:ExecutionTimeLimit") == "PT0S" &&
                value("t:Settings/t:DisallowStartIfOnBatteries") == "false";
        }

        private static bool ValidateSlotStabilization()
        {
            TaskbarSlotStabilizer state = new TaskbarSlotStabilizer();
            DateTime now = new DateTime(2026, 1, 1);
            TaskbarFreeSlot first = new TaskbarFreeSlot { Left = 166, Right = 500, IsKnown = true };
            TaskbarFreeSlot final = new TaskbarFreeSlot { Left = 166, Right = 444, IsKnown = true };
            state.Observe(first, now);
            state.Observe(final, now.AddMilliseconds(250));
            state.Observe(final, now.AddMilliseconds(500));
            if (state.Current.IsKnown) return false;
            state.Observe(final, now.AddMilliseconds(750));
            if (!state.Current.IsKnown || state.Current.Right != 444) return false;
            state.Observe(first, now.AddSeconds(1));
            if (state.Current.Right != 444) return false;
            state.Observe(first, now.AddMilliseconds(1500));
            if (state.Current.Right != 500) return false;
            state.Observe(final, now.AddSeconds(2));
            if (state.Current.Right != 444) return false;
            state.Observe(new TaskbarFreeSlot(), now.AddSeconds(3));
            return state.Current.IsKnown && state.Current.Right == 444;
        }

        internal static bool ValidateTextLayout()
        {
            foreach (int labelWidth in new int[] { 0, 20, 200, 20000 })
            foreach (int valueWidth in new int[] { 0, 25, 60, 120 })
            for (int width = 1; width <= 300; width++)
            {
                Rectangle inner = new Rectangle(7, 3, width, 20);
                MetricBarControl.MetricTextLayout layout = MetricBarControl.CalculateTextLayout(inner, labelWidth, valueWidth, 38, 29);
                foreach (Rectangle field in new Rectangle[] { layout.Label, layout.Temperature, layout.Value })
                    if (field.Width < 0 || field.Left < inner.Left || field.Right > inner.Right) return false;
                if (layout.Temperature.Width > 0)
                {
                    if (layout.Label.Width > 0 && layout.Label.Right + 2 > layout.Temperature.Left) return false;
                    if (layout.Value.Width > 0 && layout.Temperature.Right + 5 != layout.Value.Left) return false;
                    if (layout.Temperature.Width != (layout.TemperatureFormat == 1 ? 38 : 29)) return false;
                }
                else if (layout.Label.Width > 0 && layout.Value.Width > 0 && layout.Label.Right + 2 > layout.Value.Left) return false;
            }
            return MetricBarControl.CalculateTextLayout(new Rectangle(0, 0, 100, 20), 26, 25, 38, 29).TemperatureFormat == 1 &&
                MetricBarControl.CalculateTextLayout(new Rectangle(0, 0, 90, 20), 26, 25, 38, 29).TemperatureFormat == 2 &&
                MetricBarControl.CalculateTextLayout(new Rectangle(0, 0, 60, 20), 26, 25, 38, 29).TemperatureFormat == 0;
        }

        private static bool ValidateTextPixels()
        {
            foreach (float dpi in new float[] { 96, 144, 192 })
            foreach (float fontSize in new float[] { 9, 18 })
            using (Bitmap bitmap = new Bitmap(180, 60))
            using (Font font = new Font("Segoe UI", fontSize))
            {
                bitmap.SetResolution(dpi, dpi);
                Rectangle bounds = new Rectangle(20, 10, 50, 25);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Black);
                    MetricBarControl.DrawBoundedText(graphics, "CPU긴이름&" + new String('1', 300), font, bounds, Color.White, false);
                }
                bool ink = false;
                for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).ToArgb() == Color.Black.ToArgb()) continue;
                    if (!bounds.Contains(x, y)) return false;
                    ink = true;
                }
                if (!ink) return false;
            }
            return true;
        }

        private static bool ValidateDataRefresh()
        {
            foreach (string mode in new string[] { "Inside", "Above", "Popup" })
            using (MetricBarControl bar = new MetricBarControl())
            {
                AppSettings settings = AppSettings.CreateDefault();
                settings.PositionMode = mode;
                bar.Bounds = new Rectangle(10, 20, 320, 48);
                bar.Configure(settings, new MetricSnapshot { CpuPercent = 10 }, new MetricHistory());
                IntPtr handle = bar.Handle;
                Rectangle bounds = bar.Bounds;
                using (Bitmap image = new Bitmap(bar.Width, bar.Height))
                {
                    for (int i = 0; i < 20; i++)
                    {
                        bar.UpdateReadings(new MetricSnapshot { CpuPercent = i }, new MetricHistory());
                        bar.DrawToBitmap(image, bar.ClientRectangle);
                        if (bar.Handle != handle || bar.Bounds != bounds || bar.DiagnosticCpuPercent != i) return false;
                    }
                }
            }
            return true;
        }

        private static bool ValidateHistory()
        {
            MetricHistory history = new MetricHistory();
            history.Configure(600, 200);
            for (int i = 0; i < 3010; i++) history.Add(new MetricSnapshot { CpuPercent = i });
            if (history.GetValues(MetricKind.Cpu).Count != 3000 || history.GetValues(MetricKind.Cpu)[0] != 10) return false;
            history.Configure(60, 1000);
            return history.GetValues(MetricKind.Cpu).Count == 300;
        }

        private static bool ValidateSlots()
        {
            TaskbarFreeSlot normal = TaskbarLayoutProbe.ConstrainSlot(1920, 126, 500);
            TaskbarFreeSlot leftAligned = TaskbarLayoutProbe.ConstrainSlot(1920, 126, 8);
            TaskbarFreeSlot narrow = TaskbarLayoutProbe.ConstrainSlot(1920, 126, 150);
            TaskbarFreeSlot unknown = TaskbarLayoutProbe.ConstrainSlot(1920, 126, null);
            TaskbarFreeSlot outside = TaskbarLayoutProbe.ConstrainSlot(1920, 3000, 4000);
            return normal.Left == 126 && normal.Right == 500 && leftAligned.Left == leftAligned.Right &&
                narrow.Right == 150 && unknown.Right == unknown.Left && outside.Right <= 1912;
        }

        private static bool ValidateMissingDisk()
        {
            MetricSnapshot snapshot = new MetricSnapshot { DiskPercent = 73 };
            snapshot.DiskPercents["C:"] = 73;
            MetricOption option = new MetricOption { Kind = MetricKind.Disk };
            return snapshot.FormatValue(option, true, "E:") == "—" && snapshot.FormatValue(option, true, "C:") == "73%";
        }

        private static bool ValidateTimedHistory()
        {
            MetricHistory history = new MetricHistory();
            history.Configure(60, 1000);
            DateTime start = new DateTime(2026, 1, 1);
            for (int i = 0; i < 60; i++)
            {
                MetricSnapshot sample = new MetricSnapshot { Timestamp = start.AddSeconds(i * 5), CpuPercent = i };
                if (i < 59) sample.DiskPercents["E:"] = i;
                history.Add(sample);
            }
            if (history.Timestamps.Count != 12 || history.GetValues(MetricKind.Cpu)[0] != 48 ||
                !Double.IsNaN(history.GetDiskValues("E:").Last())) return false;
            history.Add(new MetricSnapshot { Timestamp = start.AddMinutes(10), CpuPercent = 99 });
            if (history.Timestamps.Count != 1 || history.GetValues(MetricKind.Cpu)[0] != 99) return false;
            history.Add(new MetricSnapshot { Timestamp = start, CpuPercent = 7 });
            return history.Timestamps.Count == 1 && history.GetValues(MetricKind.Cpu)[0] == 7;
        }

        private static bool ValidateFontHeight()
        {
            foreach (float dpi in new float[] { 96, 120, 144, 192 })
            foreach (int height in new int[] { 20, 24, 44 })
            foreach (float size in new float[] { 7, 9, 12, 18 })
            using (Bitmap bitmap = new Bitmap(200, 80))
            {
                bitmap.SetResolution(dpi, dpi);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                using (Font font = MetricBarControl.CreateHeightFittedFont(graphics, size, FontStyle.Bold, height))
                    if (MetricBarControl.MeasureFontHeight(graphics, font) > height || font.Size > size + 0.01) return false;
            }
            return true;
        }

        private static bool ValidateMissingGraphSamples()
        {
            foreach (string style in new string[] { "Line", "Fill", "Bars" })
            using (Bitmap bitmap = new Bitmap(100, 40))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                MetricOption option = AppSettings.CreateDefault().Metrics.First();
                option.GraphStyle = style;
                option.AutoScale = true;
                GraphRenderer.Draw(graphics, new Rectangle(0, 0, 100, 40),
                    new double[] { Double.NaN, 10, Double.NaN, 30, 40 }, option, Color.Gray);
                GraphRenderer.Draw(graphics, new Rectangle(0, 0, 100, 40), new double[] { Double.NaN }, option, Color.Gray);
            }
            return true;
        }

        private static bool ValidateSettingsRecovery()
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TaskbarMonitor-settings-test-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            string path = System.IO.Path.Combine(directory, "settings.json");
            try
            {
                System.IO.File.WriteAllText(path, "{broken");
                AppSettings settings = SettingsStore.LoadFromPath(path);
                return settings != null && !String.IsNullOrEmpty(SettingsStore.LoadWarning) &&
                    System.IO.File.ReadAllText(path + ".unreadable.bak") == "{broken" && System.IO.File.ReadAllText(path) == "{broken";
            }
            finally
            {
                System.IO.File.Delete(path);
                System.IO.File.Delete(path + ".unreadable.bak");
                System.IO.Directory.Delete(directory);
            }
        }

        private static bool ValidateGraphWindow()
        {
            using (Bitmap bitmap = new Bitmap(100, 40))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                MetricOption option = AppSettings.CreateDefault().Metrics.First();
                option.ColorArgb = Color.White.ToArgb();
                option.GraphStyle = "Line";
                option.AutoScale = false;
                option.FixedMaximum = 100;
                double[] values = new double[3000];
                values[0] = 100;
                graphics.Clear(Color.Black);
                GraphRenderer.Draw(graphics, new Rectangle(0, 0, 100, 40), values, option, Color.Transparent);
                if (bitmap.GetPixel(0, 1).ToArgb() == Color.Black.ToArgb()) return false;
                graphics.Clear(Color.Black);
                DateTime end = new DateTime(2026, 1, 1);
                GraphRenderer.Draw(graphics, new Rectangle(0, 0, 100, 40), new double[] { 50, 50 }, option, Color.Transparent,
                    new DateTime[] { end.AddSeconds(-50), end }, 60, 1000);
                // A long measurement pause must not draw a line through the middle.
                for (int x = 40; x < 60; x++)
                    for (int y = 0; y < bitmap.Height; y++)
                        if (bitmap.GetPixel(x, y).ToArgb() != Color.Black.ToArgb()) return false;
                return true;
            }
        }

        private static bool ValidateRequestedDefaults()
        {
            AppSettings settings = AppSettings.CreateDefault();
            return settings.PositionMode == "Inside" && settings.HiddenMeasurementMode == "Stop" &&
                !settings.OverflowPaging && !settings.PopupPinned && settings.FloatingZOrder == "Normal" &&
                settings.ShowSettingsOnManualLaunch && settings.UpdateIntervalMs == 1000 && settings.Metrics.Count == 5 &&
                settings.Metrics.First(m => m.Kind == MetricKind.Cpu).Label == "CPU" &&
                settings.Metrics.First(m => m.Kind == MetricKind.Gpu).TemperatureColorArgb == Color.FromArgb(255, 184, 74).ToArgb();
        }

        private static bool ValidateInvalidSettings()
        {
            AppSettings settings = AppSettings.CreateDefault();
            settings.Metrics.Add(null);
            settings.Metrics.Add(settings.Metrics[0].Clone());
            settings.FontSize = Single.NaN;
            settings.UpdateIntervalMs = Int32.MaxValue;
            settings.HistorySeconds = Int32.MaxValue;
            settings.MaxWidth = Int32.MaxValue;
            settings.PositionMode = "popup";
            settings.FullscreenMode = "invalid";
            settings.EnsureDefaults();
            return settings.Metrics.Count == 5 && settings.FontSize == 9 && settings.UpdateIntervalMs == 1000 &&
                settings.HistorySeconds == 60 && settings.MaxWidth == 560 && settings.PositionMode == "Popup" && settings.FullscreenMode == "Hide";
        }

        private static bool ValidateHiddenMeasurementModes()
        {
            DateTime now = new DateTime(2026, 1, 1);
            return !AppHost.ShouldSampleMetrics(true, "Stop", now.AddSeconds(-10), now) &&
                !AppHost.ShouldSampleMetrics(true, "Throttle", now.AddSeconds(-4), now) &&
                AppHost.ShouldSampleMetrics(true, "Throttle", now.AddSeconds(-5), now) &&
                AppHost.ShouldSampleMetrics(true, "Continue", now, now) && AppHost.ShouldSampleMetrics(false, "Stop", now, now);
        }

        private static bool ValidatePaging()
        {
            AppSettings settings = AppSettings.CreateDefault();
            settings.OverflowPaging = true;
            using (MetricBarControl bar = new MetricBarControl())
            using (Bitmap bitmap = new Bitmap(280, 48))
            {
                bar.Size = bitmap.Size;
                bar.SetIntegratedStyle(true, Color.Black);
                bar.Configure(settings, new MetricSnapshot(), new MetricHistory());
                bar.DrawToBitmap(bitmap, bar.ClientRectangle);
                if (bar.DiagnosticVisibleItemCount != 3 || !bar.TryNavigate(new Point(270, 10))) return false;
                bar.DrawToBitmap(bitmap, bar.ClientRectangle);
                if (bar.DiagnosticVisibleItemCount != 3 || bar.DiagnosticFirstVisibleIndex != 2) return false;
                settings.WidgetInteractionEnabled = false;
                bar.Configure(settings, new MetricSnapshot(), new MetricHistory());
                return !bar.TryNavigate(new Point(270, 10));
            }
        }

        private static bool ValidatePopupResize()
        {
            Rectangle area = new Rectangle(0, 0, 1920, 1040);
            Rectangle start = new Rectangle(100, 100, 500, 72);
            return WidgetForm.CalculatePopupResizeSize(start, -10000, -10000, area) == new Size(200, 48) &&
                WidgetForm.CalculatePopupResizeSize(start, 10000, 10000, area) == new Size(1200, 400) &&
                WidgetForm.CalculatePopupResizeSize(start, 20, 10, area) == new Size(520, 82);
        }

        private static bool ValidateSettingsFooter()
        {
            using (SettingsForm form = new SettingsForm(null, AppSettings.CreateDefault()))
            {
                form.Size = form.MinimumSize;
                TableLayoutPanel layout = form.Controls.OfType<TableLayoutPanel>().Single();
                FlowLayoutPanel footer = layout.GetControlFromPosition(0, 2) as FlowLayoutPanel;
                if (form.AutoScroll || footer == null || footer.WrapContents || footer.Dock != DockStyle.Fill ||
                    layout.RowStyles[2].SizeType != SizeType.Absolute) return false;
                int requiredWidth = footer.Padding.Horizontal + 2;
                foreach (Control child in footer.Controls)
                    requiredWidth += Math.Max(child.Width, child.GetPreferredSize(Size.Empty).Width) + child.Margin.Horizontal;
                if (requiredWidth > form.ClientSize.Width) return false;
                foreach (string text in new string[] { "저장하고 위젯 켜기", "저장·적용", "닫기", "기본 설정 복원" })
                {
                    Button button = footer.Controls.OfType<Button>().Single(b => b.Text == text);
                    if (button.Height + button.Margin.Vertical + footer.Padding.Vertical + 2 > layout.RowStyles[2].Height) return false;
                }
            }
            return true;
        }
    }
}
