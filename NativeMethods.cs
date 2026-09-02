using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TaskbarMonitor
{
    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int GWL_STYLE = -16;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_CHILD = 0x40000000;
        public const int WS_POPUP = unchecked((int)0x80000000);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public static readonly IntPtr HWND_TOP = IntPtr.Zero;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public const int WM_NCHITTEST = 0x0084;
        public const int WM_EXITSIZEMOVE = 0x0232;
        public const int HTBOTTOMRIGHT = 17;
        public static readonly int WM_APP_SHOW_SETTINGS = (int)RegisterWindowMessage("TaskbarMonitor.ShowSettings.svsivsv.v1");
        public const string MessageSinkCaption = "TaskbarMonitor.MessageSink.1";
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public Rectangle ToRectangle()
            {
                return Rectangle.FromLTRB(Left, Top, Right, Bottom);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public uint LowDateTime;
            public uint HighDateTime;

            public ulong ToUInt64()
            {
                return ((ulong)HighDateTime << 32) | LowDateTime;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string messageName);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maximumCount);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr child);

        [DllImport("user32.dll")]
        public static extern IntPtr GetTopWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr handle);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int valueSize);

        public static void EnableHighDpi()
        {
            try
            {
                SetProcessDpiAwarenessContext(new IntPtr(-4));
            }
            catch
            {
                try { SetProcessDPIAware(); }
                catch { }
            }
        }

        public static Rectangle GetPrimaryTaskbarBounds()
        {
            IntPtr taskbar = GetPrimaryTaskbarHandle();
            RECT rect;
            if (taskbar != IntPtr.Zero && GetWindowRect(taskbar, out rect))
                return rect.ToRectangle();
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            return new Rectangle(screen.Left, screen.Bottom - 48, screen.Width, 48);
        }

        public static IntPtr GetPrimaryTaskbarHandle()
        {
            return FindWindow("Shell_TrayWnd", null);
        }

        public static bool IsForegroundFullscreen(params IntPtr[] ignoredWindows)
        {
            IntPtr foreground = GetForegroundWindow();
            return IsWindowFullscreen(foreground, ignoredWindows);
        }

        public static bool IsWindowFullscreen(IntPtr foreground, params IntPtr[] ignoredWindows)
        {
            if (foreground == IntPtr.Zero || !IsWindowVisible(foreground)) return false;
            if (IsDesktopOrTaskbarWindow(foreground)) return false;
            if (ignoredWindows != null)
            {
                foreach (IntPtr ignored in ignoredWindows)
                    if (ignored != IntPtr.Zero && foreground == ignored) return false;
            }

            RECT nativeRect;
            int dwmResult = DwmGetWindowAttribute(foreground, 9, out nativeRect, Marshal.SizeOf(typeof(RECT)));
            if (dwmResult != 0 && !GetWindowRect(foreground, out nativeRect)) return false;
            Rectangle rect = nativeRect.ToRectangle();
            Rectangle bounds = Screen.FromHandle(foreground).Bounds;
            const int tolerance = 2;
            return rect.Left <= bounds.Left + tolerance && rect.Top <= bounds.Top + tolerance &&
                   rect.Right >= bounds.Right - tolerance && rect.Bottom >= bounds.Bottom - tolerance;
        }

        private static bool IsDesktopOrTaskbarWindow(IntPtr hwnd)
        {
            if (hwnd == GetDesktopWindow() || hwnd == GetShellWindow()) return true;
            StringBuilder className = new StringBuilder(256);
            if (GetClassName(hwnd, className, className.Capacity) <= 0) return false;
            string value = className.ToString();
            return String.Equals(value, "Progman", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(value, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(value, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(value, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetForegroundProcessName()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return String.Empty;
            uint processId;
            GetWindowThreadProcessId(foreground, out processId);
            if (processId == 0) return String.Empty;
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                    return process.ProcessName ?? String.Empty;
            }
            catch
            {
                return String.Empty;
            }
        }

        public static bool IsCaptureForeground()
        {
            string name = GetForegroundProcessName();
            return EqualsProcess(name, "SnippingTool") || EqualsProcess(name, "ScreenClippingHost") ||
                   EqualsProcess(name, "SnipAndSketch") || EqualsProcess(name, "Microsoft.ScreenSketch");
        }

        public static bool IsShellFlyoutForeground()
        {
            string name = GetForegroundProcessName();
            return EqualsProcess(name, "SearchHost") || EqualsProcess(name, "SearchApp") ||
                   EqualsProcess(name, "StartMenuExperienceHost") || EqualsProcess(name, "ShellExperienceHost") ||
                   EqualsProcess(name, "TextInputHost");
        }

        private static bool EqualsProcess(string actual, string expected)
        {
            return String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        public static void SetClickThrough(IntPtr hwnd, bool enabled)
        {
            if (hwnd == IntPtr.Zero) return;
            int style = GetWindowLong(hwnd, GWL_EXSTYLE);
            int updated = enabled ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
            if (updated != style) SetWindowLong(hwnd, GWL_EXSTYLE, updated);
        }
    }

    internal sealed class MessageSink : NativeWindow, IDisposable
    {
        public event EventHandler ShowSettingsRequested;

        public MessageSink()
        {
            CreateParams parameters = new CreateParams();
            parameters.Caption = NativeMethods.MessageSinkCaption;
            parameters.ExStyle = NativeMethods.WS_EX_TOOLWINDOW;
            CreateHandle(parameters);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WM_APP_SHOW_SETTINGS)
            {
                EventHandler handler = ShowSettingsRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            DestroyHandle();
        }
    }
}
