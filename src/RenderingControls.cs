using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace TaskbarMonitor
{
    internal static class GraphRenderer
    {
        public static void Draw(Graphics graphics, Rectangle bounds, IList<double> values, MetricOption option, Color gridColor,
            IList<DateTime> timestamps = null, int windowSeconds = 60, int intervalMs = 1000)
        {
            if (bounds.Width < 3 || bounds.Height < 3 || values == null || values.Count == 0) return;
            double maximum = option.AutoScale ? values.Where(IsFinite).DefaultIfEmpty(0).Max() * 1.15 : option.FixedMaximum;
            if (maximum <= 0.0001) maximum = option.Kind == MetricKind.Network ? 1024.0 : 100.0;
            maximum = Math.Max(maximum, option.Kind == MetricKind.Network ? 1024.0 : 1.0);

            if (gridColor.A > 0)
                using (Pen gridPen = new Pen(gridColor, 1.0f))
                    graphics.DrawLine(gridPen, bounds.Left, bounds.Top + bounds.Height / 2, bounds.Right, bounds.Top + bounds.Height / 2);

            bool timed = timestamps != null && timestamps.Count == values.Count;
            DateTime end = timed ? timestamps[timestamps.Count - 1] : DateTime.MinValue;
            List<PointF> segment = new List<PointF>();
            for (int index = 0; index < values.Count; index++)
            {
                bool gap = timed && index > 0 &&
                    (timestamps[index] - timestamps[index - 1]).TotalMilliseconds > Math.Max(500, intervalMs * 2.5);
                if (gap || !IsFinite(values[index]))
                {
                    DrawSegment(graphics, bounds, segment, option);
                    segment.Clear();
                    if (!IsFinite(values[index])) continue;
                }
                float fraction = timed ? (float)(1.0 - (end - timestamps[index]).TotalSeconds / Math.Max(1, windowSeconds)) :
                    (values.Count == 1 ? 1 : index / (float)(values.Count - 1));
                float x = bounds.Left + Math.Max(0, Math.Min(1, fraction)) * (bounds.Width - 1.0f);
                double normalized = Math.Max(0.0, Math.Min(1.0, values[index] / maximum));
                float y = bounds.Bottom - 1 - (float)(normalized * (bounds.Height - 2));
                segment.Add(new PointF(x, y));
            }
            DrawSegment(graphics, bounds, segment, option);
        }

        private static bool IsFinite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }

        private static void DrawSegment(Graphics graphics, Rectangle bounds, List<PointF> segment, MetricOption option)
        {
            if (segment.Count == 0) return;
            PointF[] points = segment.ToArray();
            Color color = option.Color;
            if (String.Equals(option.GraphStyle, "Bars", StringComparison.OrdinalIgnoreCase))
            {
                using (Pen pen = new Pen(Color.FromArgb(210, color), 1.0f))
                    foreach (PointF point in points) graphics.DrawLine(pen, point.X, bounds.Bottom - 1, point.X, point.Y);
                return;
            }

            if (String.Equals(option.GraphStyle, "Fill", StringComparison.OrdinalIgnoreCase) && points.Length > 1)
            {
                PointF[] polygon = new PointF[points.Length + 2];
                polygon[0] = new PointF(points[0].X, bounds.Bottom - 1);
                Array.Copy(points, 0, polygon, 1, points.Length);
                polygon[polygon.Length - 1] = new PointF(points[points.Length - 1].X, bounds.Bottom - 1);
                using (Brush fill = new SolidBrush(Color.FromArgb(72, color))) graphics.FillPolygon(fill, polygon);
            }

            if (points.Length > 1)
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(color, 1.4f)) graphics.DrawLines(pen, points);
                graphics.SmoothingMode = SmoothingMode.None;
            }
            else
                using (Brush brush = new SolidBrush(color)) graphics.FillRectangle(brush, points[0].X, points[0].Y, 1, 1);
        }
    }

    internal sealed class DisplayMetricItem
    {
        public MetricOption Option { get; set; }
        public string DiskName { get; set; }

        public string Label
        {
            get
            {
                if (!String.IsNullOrEmpty(DiskName)) return DiskName;
                return Option == null ? String.Empty : Option.Label;
            }
        }
    }

    internal static class DisplayMetricBuilder
    {
        public static List<DisplayMetricItem> Build(AppSettings settings)
        {
            List<DisplayMetricItem> items = new List<DisplayMetricItem>();
            if (settings == null || settings.Metrics == null) return items;
            foreach (MetricOption option in settings.Metrics.Where(delegate(MetricOption m) { return m.Enabled; })
                .OrderBy(delegate(MetricOption m) { return m.Order; }))
            {
                if (option.Kind == MetricKind.Disk && settings.SelectedDisks != null && settings.SelectedDisks.Count > 0)
                {
                    foreach (string diskName in settings.SelectedDisks)
                        items.Add(new DisplayMetricItem { Option = option, DiskName = diskName });
                }
                else
                    items.Add(new DisplayMetricItem { Option = option });
            }
            return items;
        }
    }

    public sealed class MetricBarControl : Control
    {
        private AppSettings settings;
        private MetricSnapshot snapshot;
        private MetricHistory history;
        private bool integratedStyle;
        private bool integratedSeamless;
        private Color integratedBackColor;
        private Rectangle previousPageBounds;
        private Rectangle nextPageBounds;
        private int pageIndex;
        private int pageCount;
        private int firstVisibleIndex;
        private int visibleItemCount;

        internal int DiagnosticPageCount { get { return pageCount; } }
        internal int DiagnosticFirstVisibleIndex { get { return firstVisibleIndex; } }
        internal int DiagnosticVisibleItemCount { get { return visibleItemCount; } }
        internal double DiagnosticCpuPercent { get { return snapshot.CpuPercent; } }

        public MetricBarControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            settings = AppSettings.CreateDefault();
            snapshot = new MetricSnapshot();
            history = new MetricHistory();
            integratedBackColor = Color.FromArgb(31, 31, 31);
            Cursor = Cursors.Hand;
        }

        public void Configure(AppSettings newSettings, MetricSnapshot newSnapshot, MetricHistory newHistory)
        {
            settings = newSettings ?? AppSettings.CreateDefault();
            snapshot = newSnapshot ?? new MetricSnapshot();
            history = newHistory ?? new MetricHistory();
            Cursor = settings.WidgetInteractionEnabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        public int GetPreferredWidth()
        {
            int count = DisplayMetricBuilder.Build(settings).Count;
            if (integratedStyle) return Math.Max(120, Math.Min(settings.MaxWidth, 4 + count * settings.InsideItemWidth));
            return Math.Max(160, Math.Min(settings.MaxWidth, 22 + count * 96));
        }

        public void UpdateReadings(MetricSnapshot newSnapshot, MetricHistory newHistory)
        {
            // Painting data must never reposition, activate or recreate a window.
            snapshot = newSnapshot ?? snapshot;
            history = newHistory ?? history;
            Invalidate();
        }

        public void SetIntegratedStyle(bool enabled, Color backgroundKey)
        {
            SetIntegratedStyle(enabled, backgroundKey, enabled);
        }

        public void SetIntegratedStyle(bool enabled, Color backgroundKey, bool seamless)
        {
            bool changed = integratedStyle != enabled || integratedSeamless != (enabled && seamless) ||
                integratedBackColor != backgroundKey;
            integratedStyle = enabled;
            integratedSeamless = enabled && seamless;
            integratedBackColor = backgroundKey;
            if (changed) Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            bool seamlessVisual = integratedStyle && integratedSeamless;
            Color background = seamlessVisual ? integratedBackColor : Color.FromArgb(settings.BackgroundArgb);
            Color foreground = Color.FromArgb(settings.ForegroundArgb);
            Color border = Color.FromArgb(settings.BorderArgb);
            using (Brush brush = new SolidBrush(background)) graphics.FillRectangle(brush, ClientRectangle);
            if (!seamlessVisual)
                using (Pen pen = new Pen(border)) graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));

            List<DisplayMetricItem> metrics = DisplayMetricBuilder.Build(settings);
            if (metrics.Count == 0)
            {
                pageCount = 1;
                pageIndex = 0;
                firstVisibleIndex = 0;
                visibleItemCount = 0;
                TextRenderer.DrawText(graphics, "표시할 항목을 선택하세요", Font, ClientRectangle, foreground,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                return;
            }

            int usableWidth = Math.Max(1, Width - 2);
            int targetWidth = integratedStyle ? settings.InsideItemWidth : 96;
            bool overflow = settings.OverflowPaging && metrics.Count * targetWidth > usableWidth;
            int navigationWidth = overflow ? (integratedStyle ? 30 : 36) : 0;
            int contentWidth = Math.Max(1, usableWidth - navigationWidth * 2);
            int pageCapacity = overflow ? Math.Max(1, contentWidth / Math.Max(48, targetWidth)) : metrics.Count;
            pageCount = overflow ? Math.Max(1, (int)Math.Ceiling(metrics.Count / (double)pageCapacity)) : 1;
            if (pageIndex >= pageCount) pageIndex = pageCount - 1;
            if (pageIndex < 0) pageIndex = 0;
            // Keep every page full when possible. For an uneven final page, slide
            // the last window back instead of leaving a single GPU/disk by itself.
            int first = overflow ? Math.Min(pageIndex * pageCapacity, Math.Max(0, metrics.Count - pageCapacity)) : 0;
            int shown = Math.Min(pageCapacity, metrics.Count - first);
            firstVisibleIndex = first;
            visibleItemCount = shown;
            int itemWidth = overflow || settings.AutoFit ? Math.Max(1, contentWidth / Math.Max(1, shown)) : targetWidth;
            int[] itemWidths = new int[shown];
            if (overflow || settings.AutoFit)
            {
                int baseWidth = Math.Max(1, contentWidth / Math.Max(1, shown));
                int remainder = Math.Max(0, contentWidth - baseWidth * shown);
                for (int index = 0; index < shown; index++)
                    itemWidths[index] = baseWidth + (index < remainder ? 1 : 0);
            }
            else
            {
                for (int index = 0; index < shown; index++) itemWidths[index] = itemWidth;
            }
            previousPageBounds = overflow ? new Rectangle(1, 1, navigationWidth, Math.Max(1, Height - 2)) : Rectangle.Empty;
            nextPageBounds = overflow ? new Rectangle(Width - navigationWidth - 1, 1, navigationWidth, Math.Max(1, Height - 2)) : Rectangle.Empty;
            if (overflow) DrawNavigation(graphics, foreground);
            float labelSize = integratedStyle ? Math.Max(7.0f, settings.FontSize - 0.7f) : settings.FontSize;
            using (Font labelFont = CreateHeightFittedFont(graphics, labelSize, FontStyle.Regular, Math.Max(1, Height - 4)))
            using (Font valueFont = CreateHeightFittedFont(graphics, Math.Max(7.0f, labelSize - 0.4f), FontStyle.Bold, Math.Max(1, Height - 4)))
            {
                int x = 1 + navigationWidth;
                for (int visibleIndex = 0; visibleIndex < shown; visibleIndex++)
                {
                    int rightLimit = Width - navigationWidth - 1;
                    int width = visibleIndex == shown - 1 && (overflow || settings.AutoFit) ? rightLimit - x : itemWidths[visibleIndex];
                    if (x >= rightLimit) break;
                    Rectangle bounds = new Rectangle(x, 1, Math.Max(1, Math.Min(width, rightLimit - x)), Math.Max(1, Height - 2));
                    GraphicsState tileState = graphics.Save();
                    try
                    {
                        graphics.SetClip(bounds, CombineMode.Intersect);
                        DrawMetric(graphics, bounds, metrics[first + visibleIndex], labelFont, valueFont, foreground, border);
                    }
                    finally { graphics.Restore(tileState); }
                    x += width;
                }
            }
            if (String.Equals(settings.PositionMode, "Popup", StringComparison.OrdinalIgnoreCase) &&
                settings.WidgetInteractionEnabled && !settings.PopupPinned)
                DrawResizeGrip(graphics, foreground);
        }

        private void DrawResizeGrip(Graphics graphics, Color foreground)
        {
            // The visible mark stays compact while WidgetForm provides a much
            // larger hit area and an explicit resize cursor.
            using (Pen pen = new Pen(Color.FromArgb(185, foreground), 1.35f))
            {
                graphics.DrawLine(pen, Width - 23, Height - 4, Width - 4, Height - 23);
                graphics.DrawLine(pen, Width - 16, Height - 4, Width - 4, Height - 16);
                graphics.DrawLine(pen, Width - 9, Height - 4, Width - 4, Height - 9);
            }
        }

        private void DrawNavigation(Graphics graphics, Color foreground)
        {
            Color arrowColor = Color.FromArgb(210, foreground);
            using (Brush button = new SolidBrush(Color.FromArgb(34, foreground)))
            {
                graphics.FillRectangle(button, previousPageBounds);
                graphics.FillRectangle(button, nextPageBounds);
            }
            using (Font navigationFont = new Font("Segoe UI", integratedStyle ? 12.0f : 14.0f, FontStyle.Bold, GraphicsUnit.Point))
            {
                TextRenderer.DrawText(graphics, "‹", navigationFont, previousPageBounds, arrowColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(graphics, "›", navigationFont, nextPageBounds, arrowColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        public bool TryNavigate(Point location)
        {
            if (!settings.WidgetInteractionEnabled || pageCount <= 1) return false;
            if (previousPageBounds.Contains(location))
            {
                pageIndex = (pageIndex - 1 + pageCount) % pageCount;
                Invalidate();
                return true;
            }
            if (nextPageBounds.Contains(location))
            {
                pageIndex = (pageIndex + 1) % pageCount;
                Invalidate();
                return true;
            }
            return false;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && TryNavigate(e.Location)) return;
            base.OnMouseClick(e);
        }

        public bool IsNavigationPoint(Point location)
        {
            return pageCount > 1 && (previousPageBounds.Contains(location) || nextPageBounds.Contains(location));
        }

        private void DrawMetric(Graphics graphics, Rectangle bounds, DisplayMetricItem item, Font labelFont,
            Font valueFont, Color foreground, Color border)
        {
            MetricOption option = item.Option;
            bool seamlessVisual = integratedStyle && integratedSeamless;
            if (bounds.Left > 1 && !seamlessVisual)
            {
                using (Pen separator = new Pen(Color.FromArgb(55, foreground)))
                    graphics.DrawLine(separator, bounds.Left, bounds.Top + 4, bounds.Left, bounds.Bottom - 4);
            }
            int padding = integratedStyle ? (integratedSeamless ? 2 : 3) : (bounds.Width < 60 ? 2 : 6);
            Rectangle inner = new Rectangle(bounds.Left + padding, bounds.Top + 1, Math.Max(1, bounds.Width - padding * 2), Math.Max(1, bounds.Height - 2));
            bool veryNarrow = bounds.Width < 62;
            string value = option.ShowValue ? snapshot.FormatValue(option, true, item.DiskName) : String.Empty;
            string temperature = snapshot.FormatTemperature(option);
            int graphThreshold = integratedStyle ? 20 : 25;
            int textHeight = Math.Min(inner.Height, Math.Max(MeasureFontHeight(graphics, labelFont), MeasureFontHeight(graphics, valueFont)));
            int measuredLabelWidth = TextRenderer.MeasureText(graphics, item.Label, labelFont,
                new Size(Int32.MaxValue, textHeight), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            int measuredValueWidth = String.IsNullOrEmpty(value) ? 0 : TextRenderer.MeasureText(graphics, value, valueFont,
                new Size(Int32.MaxValue, textHeight), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            int measuredTemperatureWidth = String.IsNullOrEmpty(temperature) ? 0 : TextRenderer.MeasureText(graphics, temperature, valueFont,
                new Size(Int32.MaxValue, textHeight), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            string compactTemperature = snapshot.FormatCompactTemperature(option);
            int compactTemperatureWidth = String.IsNullOrEmpty(compactTemperature) ? 0 :
                TextRenderer.MeasureText(graphics, compactTemperature, valueFont,
                    new Size(Int32.MaxValue, textHeight), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            MetricTextLayout layout = CalculateTextLayout(new Rectangle(inner.Left, inner.Top, inner.Width, textHeight),
                measuredLabelWidth, measuredValueWidth, measuredTemperatureWidth, compactTemperatureWidth);
            if (layout.TemperatureFormat == 2) temperature = compactTemperature;
            int temperatureWidth = layout.Temperature.Width;
            DrawBoundedText(graphics, item.Label, labelFont, layout.Label, option.Color, false);
            if (temperatureWidth > 0)
                DrawBoundedText(graphics, temperature, valueFont, layout.Temperature, option.TemperatureColor, false);
            if (option.ShowValue)
                DrawBoundedText(graphics, value, valueFont, layout.Value, foreground, true);

            if (option.ShowGraph && inner.Height >= graphThreshold && inner.Height - textHeight >= 5 && (!veryNarrow || settings.AutoFit))
            {
                Rectangle graphRect = new Rectangle(inner.Left, inner.Top + textHeight + 1, inner.Width, Math.Max(2, inner.Height - textHeight - 2));
                IList<double> values = String.IsNullOrEmpty(item.DiskName) ? history.GetValues(option.Kind) : history.GetDiskValues(item.DiskName);
                GraphRenderer.Draw(graphics, graphRect, values, option,
                    seamlessVisual ? Color.Transparent : Color.FromArgb(28, foreground),
                    history.Timestamps, history.WindowSeconds, history.IntervalMs);
            }
        }

        internal static Font CreateHeightFittedFont(Graphics graphics, float points, FontStyle style, int availableHeight)
        {
            Font font = new Font("Segoe UI", points, style, GraphicsUnit.Point);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int height = MeasureFontHeight(graphics, font);
                if (height <= availableHeight || font.Size <= 0.1f) return font;
                float next = Math.Max(0.1f, font.Size * Math.Max(0.1f, availableHeight - 1.0f) / height);
                font.Dispose();
                font = new Font("Segoe UI", next, style, GraphicsUnit.Point);
            }
            return font;
        }

        internal static int MeasureFontHeight(Graphics graphics, Font font)
        {
            return TextRenderer.MeasureText(graphics, "Ag한°C", font, new Size(10000, 10000),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;
        }

        internal static void DrawBoundedText(Graphics graphics, string text, Font font,
            Rectangle bounds, Color color, bool alignRight)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            GraphicsState state = graphics.Save();
            try
            {
                graphics.SetClip(bounds, CombineMode.Intersect);
                TextRenderer.DrawText(graphics, text, font, bounds, color,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding |
                    TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping |
                    (alignRight ? TextFormatFlags.Right : TextFormatFlags.Left));
            }
            finally { graphics.Restore(state); }
        }

        internal struct MetricTextLayout
        {
            public Rectangle Label;
            public Rectangle Temperature;
            public Rectangle Value;
            public int TemperatureFormat;
        }

        internal static MetricTextLayout CalculateTextLayout(Rectangle inner, int labelWidth,
            int measuredValueWidth, int fullTemperatureWidth, int compactTemperatureWidth)
        {
            int valueWidth = Math.Min(Math.Max(0, measuredValueWidth), inner.Width);
            int temperatureGap = valueWidth > 0 ? 5 : 0;
            int available = Math.Max(0, inner.Width - valueWidth - temperatureGap - 2 - Math.Min(labelWidth, 26));
            int format = fullTemperatureWidth > 0 && fullTemperatureWidth <= available ? 1 :
                (compactTemperatureWidth > 0 && compactTemperatureWidth <= available ? 2 : 0);
            int temperatureWidth = format == 1 ? fullTemperatureWidth : (format == 2 ? compactTemperatureWidth : 0);
            int valueLeft = inner.Right - valueWidth;
            int temperatureLeft = valueLeft - (temperatureWidth > 0 ? temperatureGap : 0) - temperatureWidth;
            int labelRight = (temperatureWidth > 0 ? temperatureLeft : valueLeft) -
                (temperatureWidth > 0 || valueWidth > 0 ? 2 : 0);
            return new MetricTextLayout {
                Label = new Rectangle(inner.Left, inner.Top, Math.Max(0, labelRight - inner.Left), inner.Height),
                Temperature = new Rectangle(temperatureLeft, inner.Top, temperatureWidth, inner.Height),
                Value = new Rectangle(valueLeft, inner.Top, valueWidth, inner.Height), TemperatureFormat = format };
        }
    }

}
