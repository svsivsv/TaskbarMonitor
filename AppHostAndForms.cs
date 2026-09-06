using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace TaskbarMonitor
{
    internal struct TaskbarFreeSlot
    {
        public int Left;
        public int Right;
        public bool IsKnown;
    }

    internal sealed class TaskbarSlotStabilizer
    {
        private TaskbarFreeSlot candidate;
        private DateTime candidateSince;
        public TaskbarFreeSlot Current { get; private set; }

        public bool Observe(TaskbarFreeSlot slot, DateTime now)
        {
            if (!slot.IsKnown) return false;
            bool changed = !candidate.IsKnown || candidate.Left != slot.Left || candidate.Right != slot.Right;
            if (changed) { candidate = slot; candidateSince = now; }
            // Shrink immediately to protect shell buttons; expand only after
            // repeated observations have agreed for half a second.
            if (Current.IsKnown)
            {
                int left = Math.Max(Current.Left, slot.Left);
                Current = new TaskbarFreeSlot { Left = left, Right = Math.Max(left, Math.Min(Current.Right, slot.Right)), IsKnown = true };
            }
            if ((now - candidateSince).TotalMilliseconds >= 500) Current = candidate;
            return changed;
        }
    }

    internal static class TaskbarLayoutProbe
    {
        private static DateTime lastQuery = DateTime.MinValue;
        private static IntPtr lastTaskbar = IntPtr.Zero;
        private static Rectangle lastBounds = Rectangle.Empty;
        private static DateTime lastVerifiedQuery = DateTime.MinValue;
        private static TaskbarFreeSlot cachedSlot;
        private static Task<ProbeResult> pending;
        private static TaskbarSlotStabilizer stabilizer = new TaskbarSlotStabilizer();
        private static DateTime settleUntil = DateTime.MinValue;
        private static int generation;
        internal static bool IsSettling { get { return DateTime.UtcNow < settleUntil; } }

        public static void NotifyLayoutChanged()
        {
            generation++;
            lastQuery = DateTime.MinValue;
            settleUntil = DateTime.UtcNow.AddSeconds(5);
        }

        private sealed class ProbeResult
        {
            public IntPtr Taskbar;
            public Rectangle Bounds;
            public TaskbarFreeSlot Slot;
            public int Generation;
        }

        public static TaskbarFreeSlot GetFreeSlot(IntPtr taskbar, Rectangle taskbarBounds, int fallbackLeft)
        {
            if (taskbar != lastTaskbar || taskbarBounds != lastBounds)
            {
                lastTaskbar = taskbar;
                lastBounds = taskbarBounds;
                lastQuery = DateTime.MinValue;
                lastVerifiedQuery = DateTime.MinValue;
                cachedSlot = new TaskbarFreeSlot();
                stabilizer = new TaskbarSlotStabilizer();
                NotifyLayoutChanged();
            }
            if (pending != null && pending.IsCompleted)
            {
                try
                {
                    ProbeResult result = pending.GetAwaiter().GetResult();
                    if (result.Taskbar == taskbar && result.Bounds == taskbarBounds && result.Generation == generation)
                    {
                        if (result.Slot.IsKnown)
                        {
                            if (stabilizer.Observe(result.Slot, DateTime.UtcNow))
                                settleUntil = DateTime.UtcNow.AddSeconds(5);
                            cachedSlot = stabilizer.Current;
                            lastVerifiedQuery = DateTime.UtcNow;
                        }
                        lastQuery = DateTime.UtcNow;
                    }
                }
                catch { lastQuery = DateTime.UtcNow; }
                pending = null;
            }
            if (pending == null && (DateTime.UtcNow - lastQuery).TotalMilliseconds >= (IsSettling ? 250 : 2000))
            {
                int queryGeneration = generation;
                pending = Task.Factory.StartNew(delegate
                {
                    return new ProbeResult { Taskbar = taskbar, Bounds = taskbarBounds,
                        Slot = Probe(taskbar, taskbarBounds, 0), Generation = queryGeneration };
                });
            }
            // One temporary shell-provider failure need not flicker the widget.
            // Never retain unverified geometry indefinitely if the provider stalls.
            bool fresh = (DateTime.UtcNow - lastVerifiedQuery).TotalSeconds < 30.0;
            return ConstrainSlot(taskbarBounds.Width, Math.Max(fallbackLeft, cachedSlot.Left),
                fresh && cachedSlot.IsKnown ? (int?)cachedSlot.Right : null);
        }

        private static TaskbarFreeSlot Probe(IntPtr taskbar, Rectangle taskbarBounds, int fallbackLeft)
        {
            int left = Math.Max(0, fallbackLeft);
            int? right = null;
            try
            {
                AutomationElement root = AutomationElement.FromHandle(taskbar);
                AutomationElement widgets = FindByAutomationId(root, "WidgetsButton");
                AutomationElement start = FindByAutomationId(root, "StartButton");
                if (widgets != null)
                {
                    System.Windows.Rect rect = widgets.Current.BoundingRectangle;
                    if (!rect.IsEmpty) left = Math.Max(left, (int)Math.Ceiling(rect.Right - taskbarBounds.Left + 8));
                }
                if (start != null)
                {
                    System.Windows.Rect rect = start.Current.BoundingRectangle;
                    if (!rect.IsEmpty) right = (int)Math.Floor(rect.Left - taskbarBounds.Left - 8);
                }
            }
            catch
            {
            }
            return ConstrainSlot(taskbarBounds.Width, left, right);
        }

        internal static TaskbarFreeSlot ConstrainSlot(int taskbarWidth, int left, int? startLeft)
        {
            int safeLeft = Math.Max(0, Math.Min(left, Math.Max(0, taskbarWidth - 8)));
            int safeRight = startLeft.HasValue ? Math.Min(startLeft.Value, taskbarWidth - 8) : safeLeft;
            // Unknown or occupied space is not a license to overlap shell buttons.
            return new TaskbarFreeSlot { Left = safeLeft, Right = Math.Max(safeLeft, safeRight), IsKnown = startLeft.HasValue };
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
        internal const int ContextMenuAutoDismissMilliseconds = 1200;
        private AppSettings settings;
        private readonly MetricSampler sampler;
        private readonly MetricHistory history;
        private MetricSnapshot snapshot;
        private readonly Timer refreshTimer;
        private readonly Timer contextMenuDismissTimer;
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip contextMenu;
        private ToolStripMenuItem interactionMenuItem;
        private ToolStripMenuItem positionModeMenuItem;
        private ToolStripMenuItem positionInsideItem;
        private ToolStripMenuItem positionAboveItem;
        private ToolStripMenuItem positionPopupItem;
        private ToolStripMenuItem floatingOrderMenuItem;
        private ToolStripMenuItem floatingOrderNormalItem;
        private ToolStripMenuItem floatingOrderTopItem;
        private ToolStripMenuItem floatingOrderBottomItem;
        private readonly MessageSink messageSink;
        private WidgetForm widgetForm;
        private SettingsForm settingsForm;
        private bool monitoring;
        private bool paused;
        private bool showSettingsAfterMenuClose;
        private int taskManagerLaunchCount;
        private DateTime lastHiddenSample;
        private DateTime nextSampleAt;
        private DateTime lastVisibilityCheck = DateTime.MinValue;
        private DateTime lastWidgetRefresh = DateTime.MinValue;
        private Task<MetricSnapshot> samplingTask;
        private bool shuttingDown;
        private bool lastFullscreen;
        private bool lastShellFlyout;
        private string lastTrayTooltip = String.Empty;
        private DateTime contextMenuPointerLeftAt = DateTime.MinValue;

        public AppHost(bool startupLaunch) : this(startupLaunch, false)
        {
        }

        internal AppHost(bool startupLaunch, bool forceWidgetStart)
        {
            settings = SettingsStore.Load();
            history = new MetricHistory();
            history.Configure(settings.HistorySeconds, settings.UpdateIntervalMs);
            sampler = new MetricSampler();
            snapshot = new MetricSnapshot();
            AppSettings initialSettings = settings.Clone();
            samplingTask = Task.Factory.StartNew(delegate { return sampler.Sample(initialSettings); },
                System.Threading.CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            nextSampleAt = DateTime.UtcNow;

            messageSink = new MessageSink();
            messageSink.ShowSettingsRequested += delegate { RequestShowSettings(); };

            contextMenu = BuildContextMenu();
            contextMenu.AutoClose = true;
            contextMenuDismissTimer = new Timer();
            contextMenuDismissTimer.Interval = 150;
            contextMenuDismissTimer.Tick += ContextMenuDismissTick;
            contextMenu.Opening += delegate
            {
                UpdatePositionModeMenu();
                floatingOrderMenuItem.Enabled = !String.Equals(settings.PositionMode, "Inside", StringComparison.OrdinalIgnoreCase);
                UpdateFloatingOrderMenu();
            };
            contextMenu.Opened += delegate
            {
                contextMenuPointerLeftAt = DateTime.MinValue;
                contextMenuDismissTimer.Start();
            };
            contextMenu.Closed += delegate
            {
                contextMenuDismissTimer.Stop();
                contextMenuPointerLeftAt = DateTime.MinValue;
                if (!showSettingsAfterMenuClose) return;
                showSettingsAfterMenuClose = false;
                try
                {
                    contextMenu.BeginInvoke((MethodInvoker)delegate { ShowSettings(); });
                }
                catch
                {
                    ShowSettings();
                }
            };
            trayIcon = new NotifyIcon();
            trayIcon.Icon = IconFactory.CreateGraphIcon(Color.FromArgb(0, 183, 195));
            trayIcon.Text = "Taskbar Monitor";
            trayIcon.Visible = true;
            trayIcon.ContextMenuStrip = contextMenu;
            trayIcon.MouseClick += TrayIconMouseClick;
            UpdateTrayTooltip();

            refreshTimer = new Timer();
            refreshTimer.Interval = Math.Max(50, Math.Min(250, settings.UpdateIntervalMs));
            refreshTimer.Tick += RefreshTick;
            refreshTimer.Start();

            if (forceWidgetStart)
                StartMonitor();
            else if (startupLaunch || !settings.ShowSettingsOnManualLaunch)
            {
                if (!String.Equals(settings.PositionMode, "Popup", StringComparison.OrdinalIgnoreCase) ||
                    settings.PopupShowOnStartup)
                    StartMonitor();
            }
            else
                ShowSettings();
        }

        public AppSettings Settings { get { return settings; } }
        public MetricSnapshot Snapshot { get { return snapshot; } }
        public MetricHistory History { get { return history; } }
        public ContextMenuStrip SharedContextMenu { get { return contextMenu; } }
        internal int TaskManagerLaunchCount { get { return taskManagerLaunchCount; } }

        private ContextMenuStrip BuildContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("위젯 설정 수정", null, delegate { RequestShowSettings(); });
            positionModeMenuItem = new ToolStripMenuItem("표시 모드");
            positionInsideItem = new ToolStripMenuItem("작업 표시줄 안쪽", null, delegate { SetPositionMode("Inside"); });
            positionAboveItem = new ToolStripMenuItem("작업 표시줄 위", null, delegate { SetPositionMode("Above"); });
            positionPopupItem = new ToolStripMenuItem("독립 팝업", null, delegate { SetPositionMode("Popup"); });
            positionModeMenuItem.DropDownItems.Add(positionInsideItem);
            positionModeMenuItem.DropDownItems.Add(positionAboveItem);
            positionModeMenuItem.DropDownItems.Add(positionPopupItem);
            menu.Items.Add(positionModeMenuItem);
            UpdatePositionModeMenu();
            menu.Items.Add("위젯 표시/숨기기", null, delegate { ToggleWidget(); });
            floatingOrderMenuItem = new ToolStripMenuItem("떠있는 창 앞뒤 순서");
            floatingOrderNormalItem = new ToolStripMenuItem("일반 창 순서 (권장)", null, delegate { SetFloatingOrder("Normal"); });
            floatingOrderTopItem = new ToolStripMenuItem("항상 위로 보내기", null, delegate { SetFloatingOrder("Top"); });
            floatingOrderBottomItem = new ToolStripMenuItem("항상 뒤로 보내기", null, delegate { SetFloatingOrder("Bottom"); });
            floatingOrderMenuItem.DropDownItems.Add(floatingOrderNormalItem);
            floatingOrderMenuItem.DropDownItems.Add(floatingOrderTopItem);
            floatingOrderMenuItem.DropDownItems.Add(floatingOrderBottomItem);
            menu.Items.Add(floatingOrderMenuItem);
            UpdateFloatingOrderMenu();
            interactionMenuItem = new ToolStripMenuItem("위젯 전체 영역 클릭 인식");
            interactionMenuItem.Checked = settings.WidgetInteractionEnabled;
            interactionMenuItem.Click += delegate
            {
                AppSettings updated = settings.Clone();
                updated.WidgetInteractionEnabled = !settings.WidgetInteractionEnabled;
                ApplySettings(updated, false);
            };
            menu.Items.Add(interactionMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("갱신 일시정지", null, delegate(object sender, EventArgs e)
            {
                paused = !paused;
                ((ToolStripMenuItem)sender).Checked = paused;
            });
            menu.Items.Add("작업 관리자 열기", null, delegate { OpenTaskManager(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, delegate { Shutdown(); });
            menu.MouseUp += ContextMenuMouseUp;
            positionModeMenuItem.DropDown.MouseUp += ContextMenuMouseUp;
            floatingOrderMenuItem.DropDown.MouseUp += ContextMenuMouseUp;
            return menu;
        }

        private void ContextMenuMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && contextMenu.Visible)
                contextMenu.Close(ToolStripDropDownCloseReason.AppClicked);
        }

        private void ContextMenuDismissTick(object sender, EventArgs e)
        {
            if (!contextMenu.Visible)
            {
                contextMenuDismissTimer.Stop();
                contextMenuPointerLeftAt = DateTime.MinValue;
                return;
            }

            if (IsPointerOverDropDown(contextMenu))
            {
                contextMenuPointerLeftAt = DateTime.MinValue;
                return;
            }

            if (contextMenuPointerLeftAt == DateTime.MinValue)
            {
                contextMenuPointerLeftAt = DateTime.UtcNow;
                return;
            }

            if ((DateTime.UtcNow - contextMenuPointerLeftAt).TotalMilliseconds >= ContextMenuAutoDismissMilliseconds)
                contextMenu.Close(ToolStripDropDownCloseReason.AppClicked);
        }

        private static bool IsPointerOverDropDown(ToolStripDropDown dropDown)
        {
            if (dropDown == null || !dropDown.Visible) return false;
            if (dropDown.Bounds.Contains(Cursor.Position)) return true;
            foreach (ToolStripItem item in dropDown.Items)
            {
                ToolStripDropDownItem dropDownItem = item as ToolStripDropDownItem;
                if (dropDownItem != null && IsPointerOverDropDown(dropDownItem.DropDown)) return true;
            }
            return false;
        }

        private void SetPositionMode(string mode)
        {
            AppSettings updated = settings.Clone();
            updated.PositionMode = mode;
            ApplySettings(updated, true);
            UpdatePositionModeMenu();
        }

        private void UpdatePositionModeMenu()
        {
            if (positionInsideItem == null) return;
            positionInsideItem.Checked = String.Equals(settings.PositionMode, "Inside", StringComparison.OrdinalIgnoreCase);
            positionAboveItem.Checked = String.Equals(settings.PositionMode, "Above", StringComparison.OrdinalIgnoreCase);
            positionPopupItem.Checked = String.Equals(settings.PositionMode, "Popup", StringComparison.OrdinalIgnoreCase);
        }

        private void SetFloatingOrder(string order)
        {
            AppSettings updated = settings.Clone();
            updated.FloatingZOrder = order;
            ApplySettings(updated, false);
            UpdateFloatingOrderMenu();
        }

        private void UpdateFloatingOrderMenu()
        {
            if (floatingOrderNormalItem == null) return;
            floatingOrderNormalItem.Checked = String.Equals(settings.FloatingZOrder, "Normal", StringComparison.OrdinalIgnoreCase);
            floatingOrderTopItem.Checked = String.Equals(settings.FloatingZOrder, "Top", StringComparison.OrdinalIgnoreCase);
            floatingOrderBottomItem.Checked = String.Equals(settings.FloatingZOrder, "Bottom", StringComparison.OrdinalIgnoreCase);
        }

        public void RequestShowSettings()
        {
            if (contextMenu != null && contextMenu.Visible)
            {
                showSettingsAfterMenuClose = true;
                contextMenu.Close();
                return;
            }
            ShowSettings();
        }

        public void ShowWidgetContextMenu(Control source, Point location)
        {
            if (source == null || source.IsDisposed) return;
            if (contextMenu.Visible)
            {
                contextMenu.Close();
                return;
            }
            contextMenu.Show(source, location);
        }

        private void TrayIconMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (String.Equals(settings.PositionMode, "Popup", StringComparison.OrdinalIgnoreCase))
            {
                if (!monitoring) StartMonitor();
                else if (widgetForm != null) widgetForm.TogglePopupVisibility();
                return;
            }
            ToggleWidget();
        }

        private void RefreshTick(object sender, EventArgs e)
        {
            if (shuttingDown) return;
            bool sampleChanged = CompleteSamplingTask();
            DateTime now = DateTime.UtcNow;
            int visibilityInterval = Math.Max(250, Math.Min(1000, settings.UpdateIntervalMs));
            if ((now - lastVisibilityCheck).TotalMilliseconds >= visibilityInterval)
            {
                lastFullscreen = NativeMethods.IsForegroundFullscreen(
                    widgetForm == null ? IntPtr.Zero : widgetForm.Handle,
                    IntPtr.Zero,
                    settingsForm == null ? IntPtr.Zero : settingsForm.Handle);
                bool captureForeground = NativeMethods.IsCaptureForeground();
                if (captureForeground && settings.CaptureMode == "Show") lastFullscreen = false;
                lastShellFlyout = NativeMethods.IsShellFlyoutForeground();
                lastVisibilityCheck = now;
            }

            if (widgetForm != null)
            {
                bool settingsActive = settingsForm != null && !settingsForm.IsDisposed && settingsForm.Visible &&
                    settingsForm.WindowState != FormWindowState.Minimized;
                widgetForm.SetSettingsOpen(settingsActive);
                widgetForm.UpdateVisibilityState(lastFullscreen, lastShellFlyout, monitoring);
                bool configurationUiOpen = contextMenu.Visible || settingsActive;
                // Keep native layout/style changes away from open menus and
                // settings, but continue painting readings without activation.
                if (sampleChanged) widgetForm.UpdateReadings(snapshot, history);
                bool layoutRefreshDue = (now - lastWidgetRefresh).TotalSeconds >= 1.0;
                if (!contextMenu.Visible) widgetForm.RefreshTaskbarPlacement();
                if (!configurationUiOpen && (sampleChanged || layoutRefreshDue))
                {
                    widgetForm.UpdateData(settings, snapshot, history);
                    lastWidgetRefresh = now;
                }
                if (ShouldMaintainTaskbarLayer(configurationUiOpen)) widgetForm.MaintainTaskbarLayer();
            }
            UpdateTrayTooltip();
            if (sampleChanged && settingsForm != null && !settingsForm.IsDisposed)
                settingsForm.UpdatePreview(snapshot, history);

            if (!paused && samplingTask == null && now >= nextSampleAt)
            {
                bool settingsVisible = settingsForm != null && !settingsForm.IsDisposed && settingsForm.Visible &&
                    settingsForm.WindowState != FormWindowState.Minimized;
                bool widgetVisible = monitoring && widgetForm != null && !widgetForm.IsDisposed && widgetForm.Visible;
                bool hidden = !settingsVisible && !widgetVisible;
                if (ShouldSampleMetrics(hidden, settings.HiddenMeasurementMode, lastHiddenSample, now))
                {
                    if (hidden && String.Equals(settings.HiddenMeasurementMode, "Throttle", StringComparison.OrdinalIgnoreCase))
                        lastHiddenSample = now;
                    AppSettings sampleSettings = settings.Clone();
                    nextSampleAt = now.AddMilliseconds(settings.UpdateIntervalMs);
                    samplingTask = Task.Factory.StartNew(
                        delegate { return sampler.Sample(sampleSettings); },
                        System.Threading.CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
                }
            }

            bool allUiHidden = (settingsForm == null || settingsForm.IsDisposed || !settingsForm.Visible ||
                settingsForm.WindowState == FormWindowState.Minimized) &&
                (widgetForm == null || widgetForm.IsDisposed || !widgetForm.Visible);
            int desiredTimerInterval = allUiHidden &&
                String.Equals(settings.HiddenMeasurementMode, "Stop", StringComparison.OrdinalIgnoreCase)
                ? 1000 : Math.Max(50, Math.Min(250, settings.UpdateIntervalMs));
            if (refreshTimer.Interval != desiredTimerInterval) refreshTimer.Interval = desiredTimerInterval;
        }

        private bool CompleteSamplingTask()
        {
            Task<MetricSnapshot> task = samplingTask;
            if (task == null || !task.IsCompleted) return false;
            samplingTask = null;
            try
            {
                snapshot = task.GetAwaiter().GetResult();
                history.Add(snapshot);
                return true;
            }
            catch (Exception ex)
            {
                Program.LogError(ex);
                return false;
            }
        }

        internal static bool ShouldSampleMetrics(bool hidden, string hiddenMeasurementMode,
            DateTime lastHiddenSampleUtc, DateTime nowUtc)
        {
            if (!hidden) return true;
            if (String.Equals(hiddenMeasurementMode, "Stop", StringComparison.OrdinalIgnoreCase)) return false;
            if (String.Equals(hiddenMeasurementMode, "Throttle", StringComparison.OrdinalIgnoreCase))
                return (nowUtc - lastHiddenSampleUtc).TotalSeconds >= 5.0;
            return true;
        }

        internal static bool ShouldMaintainTaskbarLayer(bool contextMenuVisible)
        {
            // Do not raise the owned overlay while configuration UI is active.
            return !contextMenuVisible;
        }

        private void UpdateTrayTooltip()
        {
            if (settingsForm != null && !settingsForm.IsDisposed)
                settingsForm.UpdateRuntimeStatus(GetRuntimeStatus());
            string text = "CPU " + snapshot.CpuPercent.ToString("0") + "%  RAM " + snapshot.MemoryPercent.ToString("0") + "%";
            if (settings.Metrics.Any(delegate(MetricOption m) { return m.Kind == MetricKind.Gpu && m.Enabled; }))
                text += "  GPU " + snapshot.GpuPercent.ToString("0") + "%";
            if (widgetForm != null && widgetForm.TaskbarSpaceUnavailable)
                text = "작업표시줄 빈 공간 확인 중/부족 · 우클릭으로 표시 모드 변경";
            text = text.Length > 63 ? text.Substring(0, 63) : text;
            if (String.Equals(lastTrayTooltip, text, StringComparison.Ordinal)) return;
            trayIcon.Text = text;
            lastTrayTooltip = text;
        }

        private string GetRuntimeStatus()
        {
            if (snapshot.Timestamp == DateTime.MinValue) return "첫 측정 준비 중 · 센서 초기화를 기다리는 동안 설정을 변경할 수 있습니다.";
            if (!monitoring) return "위젯 숨김 · '저장하고 위젯 켜기' 또는 트레이 아이콘으로 표시";
            if (widgetForm != null && widgetForm.TaskbarSpaceUnavailable)
                return "작업표시줄 공간 확인 중/부족 · 표시 위치를 위쪽 또는 팝업으로 바꿀 수 있습니다.";
            if (lastShellFlyout && settings.PositionMode == "Inside") return "시작·검색 메뉴가 열려 있어 위젯을 잠시 숨겼습니다.";
            if (widgetForm != null && !widgetForm.Visible) return "현재 화면/표시 설정에 따라 위젯이 숨겨져 있습니다.";
            if (paused) return "표시 중 · 측정 일시정지";
            if (samplingTask != null && (DateTime.UtcNow - snapshot.Timestamp).TotalSeconds > Math.Max(15, settings.UpdateIntervalMs / 1000.0 * 3))
                return "센서 응답 지연 · 현재 숫자는 마지막 측정값입니다. 계속되면 앱을 종료 후 다시 실행하세요.";
            return "표시 중 · CPU 온도는 ACPI 참고값, 여러 GPU의 온도·사용률은 서로 다른 장치일 수 있습니다.";
        }

        public void ApplySettings(AppSettings newSettings, bool startMonitor)
        {
            newSettings.EnsureDefaults();
            bool capturePopupLocation = newSettings.PopupPinned &&
                (!settings.PopupPinned || !newSettings.PopupPositionSaved);
            if (capturePopupLocation && widgetForm != null && !widgetForm.IsDisposed &&
                String.Equals(settings.PositionMode, "Popup", StringComparison.OrdinalIgnoreCase))
            {
                Rectangle currentBounds = widgetForm.GetScreenBounds();
                newSettings.PopupPositionSaved = true;
                newSettings.PopupX = currentBounds.X;
                newSettings.PopupY = currentBounds.Y;
            }
            AppSettings candidateSettings = newSettings.Clone();
            SettingsStore.Save(candidateSettings);
            settings = candidateSettings;
            if (interactionMenuItem != null) interactionMenuItem.Checked = settings.WidgetInteractionEnabled;
            UpdatePositionModeMenu();
            UpdateFloatingOrderMenu();
            string startupWarning = null;
            try { SettingsStore.ApplyStartupSetting(settings.StartWithWindows); }
            catch (Exception ex)
            {
                Program.LogError(ex);
                startupWarning = "위젯 설정은 저장했지만 Windows 자동 실행 설정을 변경하지 못했습니다.\n" + ex.Message;
            }
            history.Configure(settings.HistorySeconds, settings.UpdateIntervalMs);
            refreshTimer.Interval = Math.Max(50, Math.Min(250, settings.UpdateIntervalMs));
            nextSampleAt = DateTime.UtcNow;
            if (widgetForm != null) widgetForm.UpdateData(settings, snapshot, history);
            if (settings.PopupPinned && !settings.PopupPositionSaved && widgetForm != null && !widgetForm.IsDisposed)
            {
                Rectangle positionedBounds = widgetForm.GetScreenBounds();
                settings.PopupPositionSaved = true;
                settings.PopupX = positionedBounds.X;
                settings.PopupY = positionedBounds.Y;
                SettingsStore.Save(settings);
            }
            if (startMonitor) StartMonitor();
            if (startupWarning != null)
                MessageBox.Show(startupWarning, "Taskbar Monitor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public void SavePopupSize(Size size)
        {
            if (!String.Equals(settings.PositionMode, "Popup", StringComparison.OrdinalIgnoreCase)) return;
            settings.PopupWidth = Math.Max(200, Math.Min(1200, size.Width));
            settings.PopupHeight = Math.Max(48, Math.Min(400, size.Height));
            SettingsStore.Save(settings);
            if (settingsForm != null && !settingsForm.IsDisposed)
                settingsForm.SyncPopupSize(settings.PopupWidth, settings.PopupHeight);
        }

        public void StartMonitor()
        {
            if (widgetForm == null || widgetForm.IsDisposed)
                widgetForm = new WidgetForm(this);
            monitoring = true;
            widgetForm.UpdateData(settings, snapshot, history);
            widgetForm.ShowFromHost();
        }

        public void ToggleWidget()
        {
            if (monitoring && widgetForm != null && !widgetForm.IsDisposed &&
                String.Equals(settings.PositionMode, "Popup", StringComparison.OrdinalIgnoreCase))
            {
                widgetForm.TogglePopupVisibility();
                return;
            }
            if (!monitoring)
            {
                StartMonitor();
                return;
            }
            monitoring = false;
            if (widgetForm != null && !widgetForm.IsDisposed) widgetForm.Hide();
        }

        public void ShowSettings()
        {
            TaskbarLayoutProbe.NotifyLayoutChanged();
            if (widgetForm != null && !widgetForm.IsDisposed) widgetForm.SetSettingsOpen(true);
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                PositionSettingsForm(settingsForm);
                if (!settingsForm.Visible) settingsForm.Show();
                if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
                ActivateSettingsForm(settingsForm);
                return;
            }
            settingsForm = new SettingsForm(this, settings.Clone());
            settingsForm.FormClosed += delegate
            {
                TaskbarLayoutProbe.NotifyLayoutChanged();
                settingsForm = null;
                if (widgetForm != null && !widgetForm.IsDisposed) widgetForm.SetSettingsOpen(false);
            };
            PositionSettingsForm(settingsForm);
            settingsForm.Show();
            ActivateSettingsForm(settingsForm);
        }

        private static void PositionSettingsForm(Form form)
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            Rectangle area = screen.WorkingArea;
            int x = area.Left + Math.Max(0, (area.Width - form.Width) / 2);
            int y = area.Top + Math.Max(0, (area.Height - form.Height) / 2);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(x, y);
        }

        private static void ActivateSettingsForm(Form form)
        {
            if (form == null || form.IsDisposed) return;
            form.TopMost = false;
            NativeMethods.ShowWindow(form.Handle, NativeMethods.SW_RESTORE);
            // A context menu is a no-activate tool window, so Windows can reject a
            // plain foreground request and leave the settings form behind another
            // application. Pulse the form through TOPMOST once, immediately return
            // it to normal z-order, and then request foreground activation.
            NativeMethods.SetWindowPos(form.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_SHOWWINDOW);
            NativeMethods.SetWindowPos(form.Handle, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
            form.BringToFront();
            form.Activate();
            NativeMethods.SetForegroundWindow(form.Handle);
        }

        public void OpenTaskManager()
        {
            try
            {
                Process.Start("taskmgr.exe");
                taskManagerLaunchCount++;
            }
            catch (Exception ex) { MessageBox.Show("작업 관리자를 열지 못했습니다.\n" + ex.Message, "Taskbar Monitor", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        public void OpenTaskManagerFromWidget()
        {
            if (!settings.WidgetInteractionEnabled) return;
            OpenTaskManager();
        }

        public void Shutdown()
        {
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            shuttingDown = true;
            refreshTimer.Stop();
            contextMenuDismissTimer.Stop();
            Task<MetricSnapshot> activeSample = samplingTask;
            bool samplerSafeToDispose = true;
            if (activeSample != null)
            {
                try { samplerSafeToDispose = activeSample.Wait(2000); }
                catch (Exception ex) { Program.LogError(ex); }
            }
            trayIcon.Visible = false;
            trayIcon.Dispose();
            contextMenu.Dispose();
            contextMenuDismissTimer.Dispose();
            refreshTimer.Dispose();
            messageSink.Dispose();
            if (widgetForm != null) widgetForm.Dispose();
            if (settingsForm != null) settingsForm.Dispose();
            if (samplerSafeToDispose) sampler.Dispose();
            base.ExitThreadCore();
        }
    }

    public sealed class WidgetForm : Form
    {
        private const int PopupResizeGripHitSize = 44;
        private static readonly Color IntegratedBackgroundKey = Color.FromArgb(31, 31, 31);
        private readonly AppHost host;
        private readonly MetricBarControl bar;
        private bool clickThrough;
        private bool fullscreenClickThrough;
        private bool fullscreenActive;
        private bool settingsOpen;
        private bool taskbarSpaceUnavailable;
        private DateTime lastLayerMaintenance;
        private DateTime lastPlacementRefresh;
        public bool TaskbarSpaceUnavailable { get { return taskbarSpaceUnavailable; } }
        private bool embedded;
        private IntPtr taskbarParent;
        private bool hiddenForFullscreen;
        private bool hiddenForShellFlyout;
        private bool hiddenByUser;
        private bool popupDragging;
        private bool popupDragMoved;
        private bool popupResizing;
        private bool manualPopupLocation;
        private bool previousPopupPinned;
        private string previousPositionMode;
        private string previousFloatingOrder;
        private bool visualStyleInitialized;
        private bool visualSeamless;
        private int visualOpacityPercent = -1;
        private Size visualRegionSize = Size.Empty;
        private Point popupDragStartCursor;
        private Point popupDragStartLocation;
        private Point popupResizeStartCursor;
        private Rectangle popupResizeStartBounds;

        public WidgetForm(AppHost owner)
        {
            host = owner;
            Text = "Taskbar Monitor";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = false;
            BackColor = Color.FromArgb(28, 28, 28);
            bar = new MetricBarControl();
            bar.Dock = DockStyle.Fill;
            bar.MouseDown += BarMouseDown;
            bar.MouseMove += BarMouseMove;
            bar.MouseUp += BarMouseUp;
            bar.MouseLeave += delegate
            {
                if (!popupDragging && !popupResizing)
                    bar.Cursor = AcceptsWidgetInput() ? Cursors.Hand : Cursors.Default;
            };
            bar.MouseCaptureChanged += delegate
            {
                if (bar.Capture) return;
                if (popupResizing)
                {
                    popupResizing = false;
                    host.SavePopupSize(Size);
                    ApplyRoundedRegion();
                }
                popupDragging = false;
            };
            bar.MouseDoubleClick += BarMouseDoubleClick;
            bar.HandleCreated += delegate { NativeMethods.SetClickThrough(bar.Handle, !AcceptsWidgetInput()); };
            Controls.Add(bar);
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

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WM_NCHITTEST && !AcceptsWidgetInput())
            {
                message.Result = new IntPtr(NativeMethods.HTTRANSPARENT);
                return;
            }
            if (message.Msg == NativeMethods.WM_NCHITTEST && IsResizablePopup())
            {
                long packed = message.LParam.ToInt64();
                Point cursor = new Point(unchecked((short)(packed & 0xffff)), unchecked((short)((packed >> 16) & 0xffff)));
                Rectangle bounds = GetScreenBounds();
                if (cursor.X >= bounds.Right - PopupResizeGripHitSize &&
                    cursor.Y >= bounds.Bottom - PopupResizeGripHitSize)
                {
                    message.Result = new IntPtr(NativeMethods.HTBOTTOMRIGHT);
                    return;
                }
            }
            if (message.Msg == NativeMethods.WM_EXITSIZEMOVE && IsPopupMode())
            {
                base.WndProc(ref message);
                host.SavePopupSize(Size);
                ApplyRoundedRegion();
                return;
            }
            if (message.Msg == NativeMethods.WM_APP_SHOW_SETTINGS)
            {
                host.RequestShowSettings();
                message.Result = IntPtr.Zero;
                return;
            }
            if (message.Msg == NativeMethods.WM_APP_QUERY_INTERACTION_ENABLED)
            {
                message.Result = host.Settings.WidgetInteractionEnabled ? new IntPtr(1) : IntPtr.Zero;
                return;
            }
            if (message.Msg == NativeMethods.WM_APP_QUERY_TASK_MANAGER_LAUNCH_COUNT)
            {
                message.Result = new IntPtr(host.TaskManagerLaunchCount);
                return;
            }
            if (message.Msg == NativeMethods.WM_APP_QUERY_CPU_TEMPERATURE)
            {
                double? temperature = host.Snapshot.CpuTemperatureC;
                message.Result = temperature.HasValue ? new IntPtr((int)Math.Round(temperature.Value * 10.0) + 1) : IntPtr.Zero;
                return;
            }
            if (message.Msg == NativeMethods.WM_APP_QUERY_GPU_TEMPERATURE)
            {
                double? temperature = host.Snapshot.GpuTemperatureC;
                message.Result = temperature.HasValue ? new IntPtr((int)Math.Round(temperature.Value * 10.0) + 1) : IntPtr.Zero;
                return;
            }
            base.WndProc(ref message);
        }

        private void BarMouseDown(object sender, MouseEventArgs e)
        {
            if (!AcceptsWidgetInput() || e.Button != MouseButtons.Left || !IsPopupMode() || host.Settings.PopupPinned) return;
            if (IsResizeGripPoint(e.Location))
            {
                popupDragging = false;
                popupResizing = true;
                popupResizeStartCursor = Cursor.Position;
                popupResizeStartBounds = Bounds;
                bar.Cursor = Cursors.SizeNWSE;
                bar.Capture = true;
                return;
            }
            if (bar.IsNavigationPoint(e.Location)) return;
            popupDragging = true;
            popupDragMoved = false;
            popupDragStartCursor = Cursor.Position;
            popupDragStartLocation = Location;
            bar.Capture = true;
        }

        private void BarMouseMove(object sender, MouseEventArgs e)
        {
            if (!AcceptsWidgetInput()) return;
            if (popupResizing)
            {
                Point resizeCursor = Cursor.Position;
                Rectangle workingArea = Screen.FromRectangle(popupResizeStartBounds).WorkingArea;
                Size resized = CalculatePopupResizeSize(popupResizeStartBounds,
                    resizeCursor.X - popupResizeStartCursor.X, resizeCursor.Y - popupResizeStartCursor.Y, workingArea);
                if (Size != resized)
                {
                    Size = resized;
                    ApplyRoundedRegion();
                }
                return;
            }
            bar.Cursor = IsResizeGripPoint(e.Location) ? Cursors.SizeNWSE : Cursors.Hand;
            if (!popupDragging) return;
            Point cursor = Cursor.Position;
            int deltaX = cursor.X - popupDragStartCursor.X;
            int deltaY = cursor.Y - popupDragStartCursor.Y;
            if (!popupDragMoved && Math.Abs(deltaX) + Math.Abs(deltaY) < 4) return;
            popupDragMoved = true;
            manualPopupLocation = true;
            Rectangle desired = new Rectangle(popupDragStartLocation.X + deltaX, popupDragStartLocation.Y + deltaY, Width, Height);
            Location = ClampPopupBounds(desired).Location;
        }

        private void BarMouseUp(object sender, MouseEventArgs e)
        {
            if (!AcceptsWidgetInput())
            {
                popupDragging = false;
                popupResizing = false;
                bar.Capture = false;
                return;
            }
            if (e.Button == MouseButtons.Left && popupResizing)
            {
                popupResizing = false;
                bar.Capture = false;
                host.SavePopupSize(Size);
                ApplyRoundedRegion();
                bar.Cursor = IsResizeGripPoint(e.Location) ? Cursors.SizeNWSE : Cursors.Hand;
                return;
            }
            if (e.Button == MouseButtons.Left && popupDragging)
            {
                popupDragging = false;
                bar.Capture = false;
                return;
            }
            if (e.Button == MouseButtons.Right)
                host.ShowWidgetContextMenu(bar, new Point(e.X, e.Y));
        }

        private void BarMouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (AcceptsWidgetInput() && e.Button == MouseButtons.Left && !bar.IsNavigationPoint(e.Location))
                host.OpenTaskManagerFromWidget();
        }

        public void UpdateData(AppSettings settings, MetricSnapshot snapshot, MetricHistory history)
        {
            bool insideMode = String.Equals(settings.PositionMode, "Inside", StringComparison.OrdinalIgnoreCase);
            bool popupMode = String.Equals(settings.PositionMode, "Popup", StringComparison.OrdinalIgnoreCase);
            bool layerChanged = !String.Equals(previousPositionMode, settings.PositionMode, StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(previousFloatingOrder, settings.FloatingZOrder, StringComparison.OrdinalIgnoreCase);
            if (!popupMode)
            {
                hiddenByUser = false;
                manualPopupLocation = false;
            }
            else if (previousPopupPinned && !settings.PopupPinned)
                manualPopupLocation = true;
            bool seamlessRequested = insideMode && String.Equals(settings.InsideStyle, "Seamless", StringComparison.OrdinalIgnoreCase);
            bar.SetIntegratedStyle(insideMode, IntegratedBackgroundKey, seamlessRequested);
            bar.Configure(settings, snapshot, history);
            PositionWidget();
            if (layerChanged) ApplyFloatingWindowOrder(false);
            previousPopupPinned = settings.PopupPinned;
            previousPositionMode = settings.PositionMode;
            previousFloatingOrder = settings.FloatingZOrder;
            bool seamless = seamlessRequested && embedded;
            if (seamless != seamlessRequested)
                bar.SetIntegratedStyle(insideMode, IntegratedBackgroundKey, seamless);
            ApplyVisualStyle(seamless, settings.OpacityPercent);
            UpdateClickThrough();
        }

        public void UpdateReadings(MetricSnapshot snapshot, MetricHistory history)
        {
            bar.UpdateReadings(snapshot, history);
        }

        private void ApplyVisualStyle(bool seamless, int opacityPercent)
        {
            int clampedOpacity = Math.Max(25, Math.Min(100, opacityPercent));
            bool modeChanged = !visualStyleInitialized || visualSeamless != seamless;
            if (seamless)
            {
                if (modeChanged || visualOpacityPercent != 100)
                {
                    BackColor = IntegratedBackgroundKey;
                    TransparencyKey = IntegratedBackgroundKey;
                    Opacity = 1.0;
                    Region previous = Region;
                    Region = null;
                    if (previous != null) previous.Dispose();
                    visualRegionSize = Size.Empty;
                }
            }
            else
            {
                if (modeChanged)
                {
                    TransparencyKey = Color.Empty;
                    BackColor = Color.FromArgb(28, 28, 28);
                }
                if (modeChanged || visualOpacityPercent != clampedOpacity)
                    Opacity = clampedOpacity / 100.0;
                if (modeChanged || visualRegionSize != Size || Region == null)
                    ApplyRoundedRegion();
            }
            visualStyleInitialized = true;
            visualSeamless = seamless;
            visualOpacityPercent = seamless ? 100 : clampedOpacity;
        }

        public void PositionWidget()
        {
            AppSettings settings = host.Settings;
            taskbarSpaceUnavailable = false;
            IntPtr taskbarHandle = NativeMethods.GetPrimaryTaskbarHandle();
            Rectangle taskbar = NativeMethods.GetPrimaryTaskbarBounds();
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            int preferred = Math.Min(settings.MaxWidth, bar.GetPreferredWidth());
            int x = settings.TaskbarOffset;
            int width = preferred;
            int height;
            int y;

            bool popupMode = String.Equals(settings.PositionMode, "Popup", StringComparison.OrdinalIgnoreCase);
            if (String.Equals(settings.PositionMode, "Above", StringComparison.OrdinalIgnoreCase) || popupMode)
            {
                DetachFromTaskbar();
                if (popupMode)
                {
                    if (popupDragging || popupResizing) return;
                    width = Math.Max(200, Math.Min(1200, settings.PopupWidth));
                    height = Math.Max(48, Math.Min(400, settings.PopupHeight));
                    Rectangle desired;
                    if (settings.PopupPinned && settings.PopupPositionSaved)
                        desired = new Rectangle(settings.PopupX, settings.PopupY, width, height);
                    else if (manualPopupLocation && Width > 0 && Height > 0)
                        desired = new Rectangle(Left, Top, width, height);
                    else
                        desired = new Rectangle(taskbar.Right - width - 12, taskbar.Top - height - 4, width, height);
                    Rectangle bounded = ClampPopupBounds(desired);
                    x = bounded.X;
                    y = bounded.Y;
                    width = bounded.Width;
                    height = bounded.Height;
                }
                else
                {
                    height = 48;
                    x = taskbar.Left + settings.TaskbarOffset;
                    width = Math.Min(preferred, Math.Max(160, taskbar.Right - x - 12));
                    y = taskbar.Top - height - 4;
                    if (x + width > screen.Right - 8) x = Math.Max(screen.Left + 8, screen.Right - width - 8);
                }
                SetPositionIfNeeded(x, y, Math.Max(160, width), Math.Max(28, height));
            }
            else
            {
                AttachToTaskbar(taskbarHandle);
                TaskbarFreeSlot freeSlot = TaskbarLayoutProbe.GetFreeSlot(taskbarHandle, taskbar, settings.TaskbarOffset);
                if (freeSlot.Right - freeSlot.Left < 48)
                {
                    taskbarSpaceUnavailable = true;
                    if (Visible) Hide();
                    return;
                }
                x = freeSlot.Left;
                height = Math.Max(20, Math.Min(settings.InsideHeight, taskbar.Height - 6));
                int rightLimit = Math.Min(freeSlot.Right, taskbar.Width - 8);
                int available = Math.Max(1, rightLimit - x);
                width = CalculateInsideWidgetWidth(settings.AutoFit, settings.MaxWidth, width, available);
                y = Math.Max(2, (taskbar.Height - height) / 2);
                int screenX = taskbar.Left + x;
                int screenY = taskbar.Top + y;
                SetPositionIfNeeded(screenX, screenY, width, height);
            }
        }

        internal static int CalculateInsideWidgetWidth(bool autoFit, int maximumWidth, int preferredWidth, int availableWidth)
        {
            int available = Math.Max(1, availableWidth);
            int maximum = Math.Max(1, maximumWidth);
            if (autoFit) return Math.Min(maximum, available);
            return Math.Min(Math.Max(1, preferredWidth), available);
        }

        private void SetPositionIfNeeded(int x, int y, int width, int height)
        {
            Rectangle desired = new Rectangle(x, y, width, height);
            bool boundsChanged = Bounds != desired;
            if (!boundsChanged) return;

            uint flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER;
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOP, x, y, width, height, flags);
        }

        public void UpdateVisibilityState(bool fullscreen, bool shellFlyout, bool monitoring)
        {
            bool fullscreenChanged = fullscreenActive != fullscreen;
            fullscreenActive = fullscreen;
            string mode = host.Settings.FullscreenMode;
            hiddenForFullscreen = ShouldHideForEnvironment(settingsOpen, fullscreen,
                String.Equals(mode, "Hide", StringComparison.OrdinalIgnoreCase));
            fullscreenClickThrough = fullscreen && String.Equals(mode, "ClickThrough", StringComparison.OrdinalIgnoreCase);
            hiddenForShellFlyout = ShouldHideForEnvironment(settingsOpen, shellFlyout,
                String.Equals(host.Settings.PositionMode, "Inside", StringComparison.OrdinalIgnoreCase));
            UpdateClickThrough();
            ApplyAutomaticVisibility(monitoring);
            if (fullscreenChanged) ApplyFloatingWindowOrder(false);
        }

        public void MaintainTaskbarLayer()
        {
            if (!Visible || hiddenForShellFlyout) return;
            if ((DateTime.UtcNow - lastLayerMaintenance).TotalSeconds < 1.0) return;
            lastLayerMaintenance = DateTime.UtcNow;
            if (!embedded) return;
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_SHOWWINDOW);
        }

        public void RefreshTaskbarPlacement()
        {
            if ((DateTime.UtcNow - lastPlacementRefresh).TotalMilliseconds <
                (TaskbarLayoutProbe.IsSettling ? 100 : 1000)) return;
            lastPlacementRefresh = DateTime.UtcNow;
            if (String.Equals(host.Settings.PositionMode, "Inside", StringComparison.OrdinalIgnoreCase))
                PositionWidget();
        }

        private void UpdateClickThrough()
        {
            bool shouldClickThrough = !host.Settings.WidgetInteractionEnabled || fullscreenClickThrough;
            if (shouldClickThrough != clickThrough)
            {
                NativeMethods.SetClickThrough(Handle, shouldClickThrough);
                clickThrough = shouldClickThrough;
            }
            if (bar.IsHandleCreated) NativeMethods.SetClickThrough(bar.Handle, shouldClickThrough);
        }

        private bool AcceptsWidgetInput()
        {
            return ShouldAcceptWidgetInput(host.Settings.WidgetInteractionEnabled, fullscreenClickThrough);
        }

        internal static bool ShouldAcceptWidgetInput(bool interactionEnabled, bool fullscreenPassThrough)
        {
            return interactionEnabled && !fullscreenPassThrough;
        }

        internal static bool ShouldHideForEnvironment(bool settingsOpen, bool environmentActive, bool hideModeEnabled)
        {
            // Settings are not permission to cover Start/Search or fullscreen apps.
            return environmentActive && hideModeEnabled;
        }

        private void ApplyAutomaticVisibility(bool monitoring)
        {
            bool shouldShow = monitoring && !hiddenByUser && !hiddenForFullscreen && !hiddenForShellFlyout &&
                !taskbarSpaceUnavailable;
            if (shouldShow && !Visible)
            {
                Show();
                ApplyFloatingWindowOrder(false);
            }
            else if (!shouldShow && Visible) Hide();
        }

        public void TogglePopupVisibility()
        {
            hiddenByUser = !hiddenByUser;
            if (!hiddenByUser) PositionWidget();
            ApplyAutomaticVisibility(true);
            if (!hiddenByUser)
                ApplyFloatingWindowOrder(true);
        }

        public void ShowFromHost()
        {
            hiddenByUser = false;
            PositionWidget();
            ApplyAutomaticVisibility(true);
            if (Visible) ApplyFloatingWindowOrder(true);
        }

        public void ApplyFloatingWindowOrder(bool bringNormalForward)
        {
            if (embedded || String.Equals(host.Settings.PositionMode, "Inside", StringComparison.OrdinalIgnoreCase)) return;
            string order = host.Settings.FloatingZOrder ?? "Normal";
            IntPtr insertAfter;
            if (settingsOpen)
                insertAfter = NativeMethods.HWND_NOTOPMOST;
            else if (fullscreenActive && String.Equals(host.Settings.FullscreenMode, "Show", StringComparison.OrdinalIgnoreCase))
                insertAfter = NativeMethods.HWND_TOPMOST;
            else if (String.Equals(order, "Top", StringComparison.OrdinalIgnoreCase))
                insertAfter = NativeMethods.HWND_TOPMOST;
            else if (String.Equals(order, "Bottom", StringComparison.OrdinalIgnoreCase))
                insertAfter = NativeMethods.HWND_BOTTOM;
            else
                insertAfter = bringNormalForward ? NativeMethods.HWND_TOP : NativeMethods.HWND_NOTOPMOST;
            NativeMethods.SetWindowPos(Handle, insertAfter, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE |
                (Visible ? NativeMethods.SWP_SHOWWINDOW : 0));
        }

        public void SetSettingsOpen(bool open)
        {
            if (settingsOpen == open) return;
            TaskbarLayoutProbe.NotifyLayoutChanged();
            settingsOpen = open;
            ApplyFloatingWindowOrder(false);
        }

        private bool IsPopupMode()
        {
            return String.Equals(host.Settings.PositionMode, "Popup", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsResizablePopup()
        {
            return IsPopupMode() && host.Settings.WidgetInteractionEnabled && !host.Settings.PopupPinned;
        }

        private bool IsResizeGripPoint(Point location)
        {
            return IsResizablePopup() && location.X >= Math.Max(0, bar.ClientSize.Width - PopupResizeGripHitSize) &&
                location.Y >= Math.Max(0, bar.ClientSize.Height - PopupResizeGripHitSize);
        }

        internal static Size CalculatePopupResizeSize(Rectangle startBounds, int deltaX, int deltaY, Rectangle workingArea)
        {
            int maximumWidth = Math.Min(1200, Math.Max(200, workingArea.Right - startBounds.Left));
            int maximumHeight = Math.Min(400, Math.Max(48, workingArea.Bottom - startBounds.Top));
            int width = Math.Max(200, Math.Min(maximumWidth, startBounds.Width + deltaX));
            int height = Math.Max(48, Math.Min(maximumHeight, startBounds.Height + deltaY));
            return new Size(width, height);
        }

        private static Rectangle ClampPopupBounds(Rectangle desired)
        {
            Screen target = Screen.FromRectangle(desired);
            Rectangle area = target.WorkingArea;
            int width = Math.Min(Math.Max(200, desired.Width), Math.Max(200, area.Width));
            int height = Math.Min(Math.Max(48, desired.Height), Math.Max(48, area.Height));
            int x = Math.Max(area.Left, Math.Min(desired.X, area.Right - width));
            int y = Math.Max(area.Top, Math.Min(desired.Y, area.Bottom - height));
            return new Rectangle(x, y, width, height);
        }

        private void AttachToTaskbar(IntPtr taskbarHandle)
        {
            if (taskbarHandle == IntPtr.Zero)
            {
                embedded = false;
                taskbarParent = IntPtr.Zero;
                TopMost = false;
                return;
            }
            if (embedded && taskbarParent == taskbarHandle && NativeMethods.GetParent(Handle) == taskbarHandle) return;

            TopMost = false;
            int style = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_STYLE);
            style &= ~NativeMethods.WS_CHILD;
            style |= NativeMethods.WS_POPUP;
            NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_STYLE, style);
            NativeMethods.SetWindowOwner(Handle, taskbarHandle);
            embedded = NativeMethods.GetParent(Handle) == taskbarHandle;
            taskbarParent = embedded ? taskbarHandle : IntPtr.Zero;
            if (embedded)
                NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE |
                    NativeMethods.SWP_FRAMECHANGED);
        }

        private void DetachFromTaskbar()
        {
            if (!embedded)
            {
                TopMost = false;
                return;
            }
            NativeMethods.SetWindowOwner(Handle, IntPtr.Zero);
            int style = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_STYLE);
            style &= ~NativeMethods.WS_CHILD;
            style |= NativeMethods.WS_POPUP;
            NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_STYLE, style);
            embedded = false;
            taskbarParent = IntPtr.Zero;
            TopMost = false;
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_FRAMECHANGED);
        }

        public Rectangle GetScreenBounds()
        {
            return Bounds;
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
                visualRegionSize = Size;
            }
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
