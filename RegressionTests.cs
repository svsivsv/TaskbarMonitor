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
                result["productionTextLayout"] = ValidateTextLayout();
                result["textPixelClipping"] = ValidateTextPixels();
                result["dataRefreshPreservesControl"] = ValidateDataRefresh();
                result["history600SecondsAt200ms"] = ValidateHistory();
                result["taskbarDoesNotInventSpace"] = ValidateSlots();
                result["startupSlotSettlesBeforeDisplay"] = ValidateSlotStabilization();
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
            return history.GetValues(MetricKind.Cpu).Count == 60;
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
    }
}
