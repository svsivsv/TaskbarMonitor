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

    public sealed class MetricBarControl : Control
    {
        private AppSettings settings;
        private MetricSnapshot snapshot;
        private MetricHistory history;
        private bool integratedStyle;
        private Color integratedBackColor;

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
            Invalidate();
        }

        public int GetPreferredWidth()
        {
            int count = settings.Metrics.Count(delegate(MetricOption m) { return m.Enabled; });
            if (integratedStyle) return Math.Max(120, Math.Min(settings.MaxWidth, 4 + count * settings.InsideItemWidth));
            return Math.Max(160, Math.Min(settings.MaxWidth, 22 + count * 104));
        }

        public void SetIntegratedStyle(bool enabled, Color backgroundKey)
        {
            integratedStyle = enabled;
            integratedBackColor = backgroundKey;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            Color background = integratedStyle ? integratedBackColor : Color.FromArgb(settings.BackgroundArgb);
            Color foreground = Color.FromArgb(settings.ForegroundArgb);
            Color border = Color.FromArgb(settings.BorderArgb);
            using (Brush brush = new SolidBrush(background)) graphics.FillRectangle(brush, ClientRectangle);
            if (!integratedStyle)
                using (Pen pen = new Pen(border)) graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));

            List<MetricOption> metrics = settings.Metrics.Where(delegate(MetricOption m) { return m.Enabled; })
                .OrderBy(delegate(MetricOption m) { return m.Order; }).ToList();
            if (metrics.Count == 0)
            {
                TextRenderer.DrawText(graphics, "표시할 항목을 선택하세요", Font, ClientRectangle, foreground,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                return;
            }

            int usableWidth = Math.Max(1, Width - 2);
            int itemWidth = settings.AutoFit ? usableWidth / metrics.Count : 104;
            float labelSize = integratedStyle ? Math.Max(7.0f, settings.FontSize - 0.7f) : settings.FontSize;
            using (Font labelFont = new Font("Segoe UI", labelSize, FontStyle.Regular, GraphicsUnit.Point))
            using (Font valueFont = new Font("Segoe UI", Math.Max(7.0f, labelSize - 0.4f), FontStyle.Bold, GraphicsUnit.Point))
            {
                for (int index = 0; index < metrics.Count; index++)
                {
                    int x = 1 + index * itemWidth;
                    int width = index == metrics.Count - 1 && settings.AutoFit ? Width - x - 1 : itemWidth;
                    if (x >= Width - 2) break;
                    Rectangle bounds = new Rectangle(x, 1, Math.Max(1, Math.Min(width, Width - x - 1)), Math.Max(1, Height - 2));
                    DrawMetric(graphics, bounds, metrics[index], labelFont, valueFont, foreground, border);
                }
            }
        }

        private void DrawMetric(Graphics graphics, Rectangle bounds, MetricOption option, Font labelFont, Font valueFont, Color foreground, Color border)
        {
            if (bounds.Left > 1 && !integratedStyle)
            {
                using (Pen separator = new Pen(Color.FromArgb(55, foreground)))
                    graphics.DrawLine(separator, bounds.Left, bounds.Top + 4, bounds.Left, bounds.Bottom - 4);
            }
            int padding = bounds.Width < 60 ? 2 : (integratedStyle ? 3 : 6);
            Rectangle inner = new Rectangle(bounds.Left + padding, bounds.Top + 1, Math.Max(1, bounds.Width - padding * 2), Math.Max(1, bounds.Height - 2));
            bool veryNarrow = bounds.Width < 62;
            bool compact = bounds.Width < 92;
            string label = veryNarrow ? ShortLabel(option.Kind) : option.Label;
            string value = option.ShowValue ? snapshot.FormatValue(option.Kind, true) : String.Empty;
            int graphThreshold = integratedStyle ? 20 : 25;
            int textHeight = option.ShowGraph && inner.Height >= graphThreshold ? (integratedStyle ? 13 : 16) : inner.Height;
            Rectangle labelRect = new Rectangle(inner.Left, inner.Top, Math.Max(1, inner.Width / 2), textHeight);
            Rectangle valueRect = new Rectangle(labelRect.Right, inner.Top, Math.Max(1, inner.Right - labelRect.Right), textHeight);
            TextRenderer.DrawText(graphics, label, labelFont, labelRect, option.Color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (option.ShowValue)
                TextRenderer.DrawText(graphics, value, valueFont, valueRect, foreground,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            if (option.ShowGraph && inner.Height >= graphThreshold && (!veryNarrow || settings.AutoFit))
            {
                Rectangle graphRect = new Rectangle(inner.Left, inner.Top + textHeight + 1, inner.Width, Math.Max(2, inner.Height - textHeight - 2));
                GraphRenderer.Draw(graphics, graphRect, history.GetValues(option.Kind), option,
                    integratedStyle ? Color.Transparent : Color.FromArgb(28, foreground));
            }
            else if (!option.ShowValue && compact)
            {
                Rectangle centered = new Rectangle(inner.Left, inner.Top, inner.Width, inner.Height);
                TextRenderer.DrawText(graphics, label, valueFont, centered, option.Color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private static string ShortLabel(MetricKind kind)
        {
            switch (kind)
            {
                case MetricKind.Cpu: return "C";
                case MetricKind.Memory: return "M";
                case MetricKind.Disk: return "D";
                case MetricKind.Network: return "N";
                case MetricKind.Gpu: return "G";
                default: return "?";
            }
        }
    }

    public sealed class DetailGraphControl : Control
    {
        private AppSettings settings;
        private MetricSnapshot snapshot;
        private MetricHistory history;

        public DetailGraphControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            settings = AppSettings.CreateDefault();
            snapshot = new MetricSnapshot();
            history = new MetricHistory();
        }

        public void Configure(AppSettings newSettings, MetricSnapshot newSnapshot, MetricHistory newHistory)
        {
            settings = newSettings ?? AppSettings.CreateDefault();
            snapshot = newSnapshot ?? new MetricSnapshot();
            history = newHistory ?? new MetricHistory();
            int count = Math.Max(1, settings.Metrics.Count(delegate(MetricOption m) { return m.Enabled; }));
            Height = 24 + count * 94;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            Color background = Color.FromArgb(settings.BackgroundArgb);
            Color foreground = Color.FromArgb(settings.ForegroundArgb);
            using (Brush brush = new SolidBrush(background)) graphics.FillRectangle(brush, ClientRectangle);

            List<MetricOption> metrics = settings.Metrics.Where(delegate(MetricOption m) { return m.Enabled; })
                .OrderBy(delegate(MetricOption m) { return m.Order; }).ToList();
            using (Font titleFont = new Font("Segoe UI", 10.5f, FontStyle.Bold))
            using (Font valueFont = new Font("Segoe UI", 9.0f, FontStyle.Regular))
            {
                int y = 12;
                foreach (MetricOption option in metrics)
                {
                    Rectangle row = new Rectangle(12, y, Math.Max(40, Width - 24), 82);
                    string title = String.Equals(option.DisplayName, option.Label, StringComparison.OrdinalIgnoreCase)
                        ? option.Label : option.DisplayName + " · " + option.Label;
                    TextRenderer.DrawText(graphics, title, titleFont,
                        new Rectangle(row.Left, row.Top, row.Width / 2, 20), option.Color,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(graphics, snapshot.FormatValue(option.Kind, false), valueFont,
                        new Rectangle(row.Left + row.Width / 2, row.Top, row.Width / 2, 20), foreground,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    Rectangle graph = new Rectangle(row.Left, row.Top + 24, row.Width, 52);
                    using (Brush graphBack = new SolidBrush(Color.FromArgb(40, foreground))) graphics.FillRectangle(graphBack, graph);
                    GraphRenderer.Draw(graphics, graph, history.GetValues(option.Kind), option, Color.FromArgb(32, foreground));
                    y += 94;
                }
            }
        }
    }
}
