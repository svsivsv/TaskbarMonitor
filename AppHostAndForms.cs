using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Automation;
using System.Windows.Forms;

namespace TaskbarMonitor
{
    internal struct TaskbarFreeSlot
    {
        public int Left;
        public int Right;
    }

    internal static class TaskbarLayoutProbe
    {
        private static DateTime lastQuery = DateTime.MinValue;
        private static IntPtr lastTaskbar = IntPtr.Zero;
        private static Rectangle lastBounds = Rectangle.Empty;
        private static TaskbarFreeSlot cachedSlot;

        public static TaskbarFreeSlot GetFreeSlot(IntPtr taskbar, Rectangle taskbarBounds, int fallbackLeft)
        {
            if (taskbar == lastTaskbar && taskbarBounds == lastBounds && (DateTime.UtcNow - lastQuery).TotalSeconds < 10.0)
                return cachedSlot;

            TaskbarFreeSlot slot = new TaskbarFreeSlot();
            slot.Left = fallbackLeft;
            slot.Right = taskbarBounds.Width / 2 - 72;
            try
            {
                AutomationElement root = AutomationElement.FromHandle(taskbar);
                AutomationElement widgets = FindByAutomationId(root, "WidgetsButton");
                AutomationElement start = FindByAutomationId(root, "StartButton");
                if (widgets != null)
                {
                    System.Windows.Rect rect = widgets.Current.BoundingRectangle;
                    if (!rect.IsEmpty) slot.Left = Math.Max(slot.Left, (int)Math.Ceiling(rect.Right - taskbarBounds.Left + 8));
                }
                if (start != null)
                {
                    System.Windows.Rect rect = start.Current.BoundingRectangle;
                    if (!rect.IsEmpty) slot.Right = (int)Math.Floor(rect.Left - taskbarBounds.Left - 8);
                }
            }
            catch
            {
            }
            if (slot.Right <= slot.Left + 80) slot.Right = taskbarBounds.Width / 2 - 72;
            lastQuery = DateTime.UtcNow;
            lastTaskbar = taskbar;
            lastBounds = taskbarBounds;
            cachedSlot = slot;
            return slot;
        }

        private static AutomationElement FindByAutomationId(AutomationElement root, string automationId)
        {
            if (root == null) return null;
            PropertyCondition condition = new PropertyCondition(AutomationElement.AutomationIdProperty, automationId);
            return root.FindFirst(TreeScope.Descendants, condition);
        }
    }

    public sealed class AppHost : ApplicationContext
    {
        private AppSettings settings;
        private readonly MetricSampler sampler;
        private readonly MetricHistory history;
        private MetricSnapshot snapshot;
        private readonly Timer refreshTimer;
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip contextMenu;
        private readonly MessageSink messageSink;
        private WidgetForm widgetForm;
        private DetailForm detailForm;
        private SettingsForm settingsForm;
        private bool monitoring;
        private bool paused;
        private DateTime lastHiddenSample;

        public AppHost(bool startupLaunch)
        {
            settings = SettingsStore.Load();
            history = new MetricHistory();
            history.Configure(settings.HistorySeconds, settings.UpdateIntervalMs);
            sampler = new MetricSampler();
            snapshot = sampler.Sample(settings);
            history.Add(snapshot);

            messageSink = new MessageSink();
            messageSink.ShowSettingsRequested += delegate { ShowSettings(); };

            contextMenu = BuildContextMenu();
            trayIcon = new NotifyIcon();
            trayIcon.Icon = IconFactory.CreateGraphIcon(Color.FromArgb(0, 183, 195));
            trayIcon.Text = "Taskbar Monitor";
            trayIcon.Visible = true;
            trayIcon.ContextMenuStrip = contextMenu;
            trayIcon.MouseClick += TrayIconMouseClick;

            refreshTimer = new Timer();
            refreshTimer.Interval = settings.UpdateIntervalMs;
            refreshTimer.Tick += RefreshTick;
            refreshTimer.Start();

            if (startupLaunch || !settings.ShowSettingsOnManualLaunch)
                StartMonitor();
            else
                ShowSettings();
        }

        public AppSettings Settings { get { return settings; } }
        public MetricSnapshot Snapshot { get { return snapshot; } }
        public MetricHistory History { get { return history; } }
        public ContextMenuStrip SharedContextMenu { get { return contextMenu; } }

        private ContextMenuStrip BuildContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("설정 열기", null, delegate { ShowSettings(); });
            menu.Items.Add("상세 그래프 열기/닫기", null, delegate { ToggleDetail(); });
            menu.Items.Add("위젯 표시/숨기기", null, delegate { ToggleWidget(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("갱신 일시정지", null, delegate(object sender, EventArgs e)
            {
                paused = !paused;
                ((ToolStripMenuItem)sender).Checked = paused;
            });
            menu.Items.Add("작업 관리자 열기", null, delegate { OpenTaskManager(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, delegate { Shutdown(); });
            return menu;
        }

        private void TrayIconMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (monitoring) ToggleDetail();
            else ShowSettings();
        }

        private void RefreshTick(object sender, EventArgs e)
        {
            bool fullscreen = NativeMethods.IsForegroundFullscreen(
                widgetForm == null ? IntPtr.Zero : widgetForm.Handle,
                detailForm == null ? IntPtr.Zero : detailForm.Handle,
                settingsForm == null ? IntPtr.Zero : settingsForm.Handle);
            bool captureForeground = NativeMethods.IsCaptureForeground();
            if (captureForeground && settings.CaptureMode == "Show") fullscreen = false;
            bool shellFlyoutForeground = NativeMethods.IsShellFlyoutForeground();

            if (!paused)
            {
                bool hiddenAndThrottled = settings.PauseWhenHidden && fullscreen && settings.FullscreenMode == "Hide";
                if (!hiddenAndThrottled || (DateTime.UtcNow - lastHiddenSample).TotalSeconds >= 5.0)
                {
                    snapshot = sampler.Sample(settings);
                    history.Add(snapshot);
                    if (hiddenAndThrottled) lastHiddenSample = DateTime.UtcNow;
                }
            }

            UpdateTrayTooltip();
            if (widgetForm != null)
            {
                widgetForm.UpdateData(settings, snapshot, history);
                widgetForm.UpdateFullscreenState(fullscreen, monitoring);
                widgetForm.UpdateShellFlyoutState(shellFlyoutForeground, monitoring);
            }
            if (detailForm != null)
            {
                detailForm.UpdateData(settings, snapshot, history);
                if (fullscreen && settings.FullscreenMode == "Hide") detailForm.Hide();
            }
            if (settingsForm != null && !settingsForm.IsDisposed)
                settingsForm.UpdatePreview(snapshot, history);
        }

        private void UpdateTrayTooltip()
        {
            string text = "CPU " + snapshot.CpuPercent.ToString("0") + "%  RAM " + snapshot.MemoryPercent.ToString("0") + "%";
            if (settings.Metrics.Any(delegate(MetricOption m) { return m.Kind == MetricKind.Gpu && m.Enabled; }))
                text += "  GPU " + snapshot.GpuPercent.ToString("0") + "%";
            trayIcon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        public void ApplySettings(AppSettings newSettings, bool startMonitor)
        {
            newSettings.EnsureDefaults();
            settings = newSettings.Clone();
            SettingsStore.Save(settings);
            SettingsStore.ApplyStartupSetting(settings.StartWithWindows);
            history.Configure(settings.HistorySeconds, settings.UpdateIntervalMs);
            refreshTimer.Interval = Math.Max(200, Math.Min(10000, settings.UpdateIntervalMs));
            if (widgetForm != null) widgetForm.UpdateData(settings, snapshot, history);
            if (detailForm != null) detailForm.UpdateData(settings, snapshot, history);
            if (startMonitor) StartMonitor();
        }

        public void StartMonitor()
        {
            if (widgetForm == null || widgetForm.IsDisposed)
                widgetForm = new WidgetForm(this);
            monitoring = true;
            widgetForm.UpdateData(settings, snapshot, history);
            widgetForm.PositionWidget();
            if (!widgetForm.Visible) widgetForm.Show();
        }

        public void ToggleWidget()
        {
            if (!monitoring)
            {
                StartMonitor();
                return;
            }
            monitoring = false;
            if (widgetForm != null && !widgetForm.IsDisposed) widgetForm.Hide();
            if (detailForm != null && !detailForm.IsDisposed) detailForm.Hide();
        }

        public void ToggleDetail()
        {
            if (!monitoring)
            {
                StartMonitor();
                return;
            }
            if (detailForm == null || detailForm.IsDisposed)
                detailForm = new DetailForm(this);
            detailForm.UpdateData(settings, snapshot, history);
            if (detailForm.Visible) detailForm.Hide();
            else
            {
                detailForm.PositionNear(widgetForm);
                detailForm.Show();
                detailForm.BringToFront();
            }
        }

        public void ShowSettings()
        {
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                if (!settingsForm.Visible) settingsForm.Show();
                if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
                settingsForm.Activate();
                settingsForm.BringToFront();
                return;
            }
            settingsForm = new SettingsForm(this, settings.Clone());
            settingsForm.FormClosed += delegate { settingsForm = null; };
            settingsForm.Show();
            settingsForm.Activate();
        }

        public void OpenTaskManager()
        {
            try { Process.Start("taskmgr.exe"); }
            catch (Exception ex) { MessageBox.Show("작업 관리자를 열지 못했습니다.\n" + ex.Message, "Taskbar Monitor", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        public void Shutdown()
        {
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            refreshTimer.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            contextMenu.Dispose();
            messageSink.Dispose();
            if (widgetForm != null) widgetForm.Dispose();
            if (detailForm != null) detailForm.Dispose();
            if (settingsForm != null) settingsForm.Dispose();
            sampler.Dispose();
            base.ExitThreadCore();
        }
    }

    public sealed class WidgetForm : Form
    {
        private static readonly Color IntegratedBackgroundKey = Color.FromArgb(31, 31, 31);
        private readonly AppHost host;
        private readonly MetricBarControl bar;
        private bool clickThrough;
        private bool embedded;
        private IntPtr taskbarParent;
        private bool hiddenForFullscreen;
        private bool hiddenForShellFlyout;

        public WidgetForm(AppHost owner)
        {
            host = owner;
            Text = "Taskbar Monitor";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.FromArgb(28, 28, 28);
            bar = new MetricBarControl();
            bar.Dock = DockStyle.Fill;
            bar.MouseClick += BarMouseClick;
            bar.MouseDoubleClick += BarMouseDoubleClick;
            Controls.Add(bar);
            ContextMenuStrip = owner.SharedContextMenu;
            bar.ContextMenuStrip = owner.SharedContextMenu;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
                return parameters;
            }
        }

        private void BarMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Clicks == 1) host.ToggleDetail();
        }

        private void BarMouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) host.OpenTaskManager();
        }

        public void UpdateData(AppSettings settings, MetricSnapshot snapshot, MetricHistory history)
        {
            bool insideMode = String.Equals(settings.PositionMode, "Inside", StringComparison.OrdinalIgnoreCase);
            bar.SetIntegratedStyle(insideMode, IntegratedBackgroundKey);
            bar.Configure(settings, snapshot, history);
            PositionWidget();
            bool seamless = insideMode && embedded && String.Equals(settings.InsideStyle, "Seamless", StringComparison.OrdinalIgnoreCase);
            if (seamless)
            {
                BackColor = IntegratedBackgroundKey;
                TransparencyKey = IntegratedBackgroundKey;
                Opacity = 1.0;
                Region previous = Region;
                Region = null;
                if (previous != null) previous.Dispose();
            }
            else
            {
                TransparencyKey = Color.Empty;
                BackColor = Color.FromArgb(28, 28, 28);
                Opacity = Math.Max(0.25, Math.Min(1.0, settings.OpacityPercent / 100.0));
                ApplyRoundedRegion();
            }
        }

        public void PositionWidget()
        {
            AppSettings settings = host.Settings;
            IntPtr taskbarHandle = NativeMethods.GetPrimaryTaskbarHandle();
            Rectangle taskbar = NativeMethods.GetPrimaryTaskbarBounds();
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            int preferred = Math.Min(settings.MaxWidth, bar.GetPreferredWidth());
            int x = settings.TaskbarOffset;
            int width = preferred;
            int height;
            int y;

            if (String.Equals(settings.PositionMode, "Above", StringComparison.OrdinalIgnoreCase))
            {
                DetachFromTaskbar();
                x = taskbar.Left + settings.TaskbarOffset;
                height = 48;
                width = Math.Min(preferred, Math.Max(160, taskbar.Right - x - 12));
                y = taskbar.Top - height - 4;
                if (x + width > screen.Right - 8) x = Math.Max(screen.Left + 8, screen.Right - width - 8);
                NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOP, x, y, Math.Max(160, width), Math.Max(28, height),
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
            }
            else
            {
                AttachToTaskbar(taskbarHandle);
                TaskbarFreeSlot freeSlot = TaskbarLayoutProbe.GetFreeSlot(taskbarHandle, taskbar, settings.TaskbarOffset);
                x = freeSlot.Left;
                height = Math.Max(20, Math.Min(settings.InsideHeight, taskbar.Height - 6));
                int centerLimit = freeSlot.Right;
                int available = centerLimit - x;
                if (settings.AutoFit && available >= 160) width = Math.Min(width, available);
                width = Math.Min(width, Math.Max(160, taskbar.Width - x - 220));
                y = Math.Max(2, (taskbar.Height - height) / 2);
                if (embedded)
                {
                    NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOP, x, y, Math.Max(160, width), Math.Max(28, height),
                        NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
                }
                else
                {
                    int screenX = taskbar.Left + x;
                    int screenY = taskbar.Top + y;
                    NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOP, screenX, screenY, Math.Max(160, width), Math.Max(28, height),
                        NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
                }
            }
        }

        public void UpdateFullscreenState(bool fullscreen, bool monitoring)
        {
            string mode = host.Settings.FullscreenMode;
            hiddenForFullscreen = fullscreen && String.Equals(mode, "Hide", StringComparison.OrdinalIgnoreCase);
            bool shouldClickThrough = fullscreen && String.Equals(mode, "ClickThrough", StringComparison.OrdinalIgnoreCase);
            if (shouldClickThrough != clickThrough)
            {
                NativeMethods.SetClickThrough(Handle, shouldClickThrough);
                clickThrough = shouldClickThrough;
            }
            ApplyAutomaticVisibility(monitoring);
        }

        public void UpdateShellFlyoutState(bool shellFlyoutForeground, bool monitoring)
        {
            hiddenForShellFlyout = shellFlyoutForeground && !embedded;
            ApplyAutomaticVisibility(monitoring);
        }

        private void ApplyAutomaticVisibility(bool monitoring)
        {
            bool shouldShow = monitoring && !hiddenForFullscreen && !hiddenForShellFlyout;
            if (shouldShow && !Visible) Show();
            else if (!shouldShow && Visible) Hide();
        }

        private void AttachToTaskbar(IntPtr taskbarHandle)
        {
            if (taskbarHandle == IntPtr.Zero)
            {
                embedded = false;
                taskbarParent = IntPtr.Zero;
                TopMost = true;
                return;
            }
            if (embedded && taskbarParent == taskbarHandle && NativeMethods.GetParent(Handle) == taskbarHandle) return;

            TopMost = false;
            NativeMethods.SetParent(Handle, taskbarHandle);
            int style = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_STYLE);
            style |= NativeMethods.WS_CHILD;
            style &= ~NativeMethods.WS_POPUP;
            NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_STYLE, style);
            embedded = NativeMethods.GetParent(Handle) == taskbarHandle;
            taskbarParent = embedded ? taskbarHandle : IntPtr.Zero;
            if (!embedded) TopMost = true;
        }

        private void DetachFromTaskbar()
        {
            if (!embedded)
            {
                TopMost = true;
                return;
            }
            NativeMethods.SetParent(Handle, IntPtr.Zero);
            int style = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_STYLE);
            style &= ~NativeMethods.WS_CHILD;
            style |= NativeMethods.WS_POPUP;
            NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_STYLE, style);
            embedded = false;
            taskbarParent = IntPtr.Zero;
            TopMost = true;
        }

        public Rectangle GetScreenBounds()
        {
            if (!embedded) return Bounds;
            Point screenPoint = PointToScreen(Point.Empty);
            return new Rectangle(screenPoint, Size);
        }

        private void ApplyRoundedRegion()
        {
            int radius = Math.Min(10, Math.Min(Width, Height) / 3);
            using (GraphicsPath path = new GraphicsPath())
            {
                int diameter = radius * 2;
                Rectangle arc = new Rectangle(0, 0, diameter, diameter);
                path.AddArc(arc, 180, 90);
                arc.X = Width - diameter - 1;
                path.AddArc(arc, 270, 90);
                arc.Y = Height - diameter - 1;
                path.AddArc(arc, 0, 90);
                arc.X = 0;
                path.AddArc(arc, 90, 90);
                path.CloseFigure();
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }
    }

    public sealed class DetailForm : Form
    {
        private readonly AppHost host;
        private readonly DetailGraphControl graph;

        public DetailForm(AppHost owner)
        {
            host = owner;
            Text = "Taskbar Monitor - 상세 그래프";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            MinimumSize = new Size(390, 240);
            Size = new Size(560, 520);
            BackColor = Color.FromArgb(28, 28, 28);
            AutoScroll = true;
            graph = new DetailGraphControl();
            graph.Location = Point.Empty;
            graph.Width = ClientSize.Width;
            graph.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(graph);
            ContextMenuStrip = owner.SharedContextMenu;
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Hide(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
        }

        public void UpdateData(AppSettings settings, MetricSnapshot snapshot, MetricHistory history)
        {
            Opacity = Math.Max(0.35, Math.Min(1.0, settings.OpacityPercent / 100.0));
            graph.Configure(settings, snapshot, history);
            graph.Width = Math.Max(100, ClientSize.Width - (VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
        }

        public void PositionNear(WidgetForm widget)
        {
            Rectangle working = Screen.PrimaryScreen.WorkingArea;
            Rectangle widgetBounds = widget == null ? Rectangle.Empty : widget.GetScreenBounds();
            int x = widget == null ? working.Right - Width - 18 : widgetBounds.Left;
            int y = widget == null ? working.Bottom - Height - 18 : widgetBounds.Top - Height - 6;
            if (x + Width > working.Right) x = working.Right - Width;
            if (x < working.Left) x = working.Left;
            if (y < working.Top) y = working.Top;
            Location = new Point(x, y);
        }
    }

    internal static class IconFactory
    {
        public static Icon CreateGraphIcon(Color color)
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(24, 24, 24));
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(color, 3.0f))
                {
                    graphics.DrawLines(pen, new Point[]
                    {
                        new Point(2, 25), new Point(8, 22), new Point(12, 9),
                        new Point(17, 23), new Point(22, 16), new Point(29, 19)
                    });
                }
                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { NativeMethods.DestroyIcon(handle); }
            }
        }
    }
}
