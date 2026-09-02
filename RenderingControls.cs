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
        public static void Draw(Graphics graphics, Rectangle bounds, IList<double> values, MetricOption option, Color gridColor)
        {
            if (bounds.Width < 3 || bounds.Height < 3 || values == null || values.Count == 0) return;
            double maximum = option.AutoScale ? values.Max() * 1.15 : option.FixedMaximum;
            if (maximum <= 0.0001) maximum = option.Kind == MetricKind.Network ? 1024.0 : 100.0;
            maximum = Math.Max(maximum, option.Kind == MetricKind.Network ? 1024.0 : 1.0);

            if (gridColor.A > 0)
                using (Pen gridPen = new Pen(gridColor, 1.0f))
                    graphics.DrawLine(gridPen, bounds.Left, bounds.Top + bounds.Height / 2, bounds.Right, bounds.Top + bounds.Height / 2);

            int pointCount = Math.Min(values.Count, Math.Max(2, bounds.Width));
            PointF[] points = new PointF[pointCount];
            int start = values.Count - pointCount;
            for (int index = 0; index < pointCount; index++)
            {
                float x = pointCount == 1 ? bounds.Left : bounds.Left + index * (bounds.Width - 1.0f) / (pointCount - 1.0f);
                double normalized = Math.Max(0.0, Math.Min(1.0, values[start + index] / maximum));
                float y = bounds.Bottom - 1 - (float)(normalized * (bounds.Height - 2));
                points[index] = new PointF(x, y);
            }

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

        public void SetIntegratedStyle(bool enabled, Color backgroundKey)
        {
            SetIntegratedStyle(enabled, backgroundKey, enabled);
        }

        public void SetIntegratedStyle(bool enabled, Color backgroundKey, bool seamless)
        {
            integratedStyle = enabled;
            integratedSeamless = enabled && seamless;
            integratedBackColor = backgroundKey;
            Invalidate();
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
            previousPageBounds = overflow ? new Rectangle(1, 1, navigationWidth, Math.Max(1, Height - 2)) : Rectangle.Empty;
            nextPageBounds = overflow ? new Rectangle(Width - navigationWidth - 1, 1, navigationWidth, Math.Max(1, Height - 2)) : Rectangle.Empty;
            if (overflow) DrawNavigation(graphics, foreground);
            float labelSize = integratedStyle ? Math.Max(7.0f, settings.FontSize - 0.7f) : settings.FontSize;
            using (Font labelFont = new Font("Segoe UI", labelSize, FontStyle.Regular, GraphicsUnit.Point))
            using (Font valueFont = new Font("Segoe UI", Math.Max(7.0f, labelSize - 0.4f), FontStyle.Bold, GraphicsUnit.Point))
            {
                for (int visibleIndex = 0; visibleIndex < shown; visibleIndex++)
                {
                    int x = 1 + navigationWidth + visibleIndex * itemWidth;
                    int rightLimit = Width - navigationWidth - 1;
                    int width = visibleIndex == shown - 1 && (overflow || settings.AutoFit) ? rightLimit - x : itemWidth;
                    if (x >= rightLimit) break;
                    Rectangle bounds = new Rectangle(x, 1, Math.Max(1, Math.Min(width, rightLimit - x)), Math.Max(1, Height - 2));
                    DrawMetric(graphics, bounds, metrics[first + visibleIndex], labelFont, valueFont, foreground, border);
                }
            }
            if (String.Equals(settings.PositionMode, "Popup", StringComparison.OrdinalIgnoreCase) &&
                settings.WidgetInteractionEnabled && !settings.PopupPinned)
                DrawResizeGrip(graphics, foreground);
        }

        private void DrawResizeGrip(Graphics graphics, Color foreground)
        {
            // Keep the visible mark small so it does not cover the final graph.
            // The actual hit-test area is intentionally larger in WidgetForm.
            using (Pen pen = new Pen(Color.FromArgb(185, foreground), 1.35f))
            {
                graphics.DrawLine(pen, Width - 15, Height - 4, Width - 4, Height - 15);
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
            if (pageCount <= 1) return false;
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

        private void DrawMetric(Graphics graphics, Rectangle bounds, DisplayMetricItem item, Font labelFont, Font valueFont, Color foreground, Color border)
        {
            MetricOption option = item.Option;
            bool seamlessVisual = integratedStyle && integratedSeamless;
            if (bounds.Left > 1 && !seamlessVisual)
            {
                using (Pen separator = new Pen(Color.FromArgb(55, foreground)))
                    graphics.DrawLine(separator, bounds.Left, bounds.Top + 4, bounds.Left, bounds.Bottom - 4);
            }
            int padding = bounds.Width < 60 ? 2 : (integratedStyle ? 3 : 6);
            Rectangle inner = new Rectangle(bounds.Left + padding, bounds.Top + 1, Math.Max(1, bounds.Width - padding * 2), Math.Max(1, bounds.Height - 2));
            bool veryNarrow = bounds.Width < 62;
            bool compact = bounds.Width < 92;
            string value = option.ShowValue ? snapshot.FormatValue(option, true, item.DiskName) : String.Empty;
            int graphThreshold = integratedStyle ? 20 : 25;
            int textHeight = option.ShowGraph && inner.Height >= graphThreshold ? (integratedStyle ? 13 : 16) : inner.Height;
            int halfWidth = Math.Max(1, inner.Width / 2);
            int minimumValueWidth = option.ShowValue ? Math.Min(28, halfWidth) : 0;
            int maximumLabelWidth = Math.Max(1, inner.Width - minimumValueWidth);
            int measuredLabelWidth = TextRenderer.MeasureText(item.Label, labelFont,
                new Size(Int32.MaxValue, textHeight), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            int labelWidth = Math.Min(maximumLabelWidth, Math.Max(halfWidth, measuredLabelWidth));
            Rectangle labelRect = new Rectangle(inner.Left, inner.Top, labelWidth, textHeight);
            Rectangle valueRect = new Rectangle(labelRect.Right, inner.Top, Math.Max(1, inner.Right - labelRect.Right), textHeight);
            TextRenderer.DrawText(graphics, item.Label, labelFont, labelRect, option.Color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (option.ShowValue)
                TextRenderer.DrawText(graphics, value, valueFont, valueRect, foreground,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            if (option.ShowGraph && inner.Height >= graphThreshold && (!veryNarrow || settings.AutoFit))
            {
                Rectangle graphRect = new Rectangle(inner.Left, inner.Top + textHeight + 1, inner.Width, Math.Max(2, inner.Height - textHeight - 2));
                IList<double> values = String.IsNullOrEmpty(item.DiskName) ? history.GetValues(option.Kind) : history.GetDiskValues(item.DiskName);
                GraphRenderer.Draw(graphics, graphRect, values, option,
                    seamlessVisual ? Color.Transparent : Color.FromArgb(28, foreground));
            }
            else if (!option.ShowValue && compact)
            {
                Rectangle centered = new Rectangle(inner.Left, inner.Top, inner.Width, inner.Height);
                TextRenderer.DrawText(graphics, item.Label, valueFont, centered, option.Color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

}
