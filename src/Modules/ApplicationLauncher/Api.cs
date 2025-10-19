using System.Diagnostics;
using System.Runtime.InteropServices;
// ReSharper disable InconsistentNaming

namespace Axorith.Module.ApplicationLauncher.Windows
{
    public static class Api
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        private static RECT[] GetMonitors()
        {
            var monitors = new System.Collections.Generic.List<RECT>();

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
                {
                    MONITORINFO mi = new MONITORINFO();
                    mi.cbSize = (uint)Marshal.SizeOf(typeof(MONITORINFO));
                    if (GetMonitorInfo(hMonitor, ref mi))
                    {
                        monitors.Add(mi.rcMonitor);
                    }
                    return true;
                }, IntPtr.Zero);

            return monitors.ToArray();
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        public static void MoveProcessWindowToMonitor(Process process, int monitorIndex)
        {
            if (process == null || process.HasExited) throw new ArgumentException("Process is null or exited.");
            if (monitorIndex < 0) throw new ArgumentOutOfRangeException(nameof(monitorIndex));

            RECT[] monitors = GetMonitors();
            if (monitorIndex >= monitors.Length) throw new ArgumentOutOfRangeException(nameof(monitorIndex));

            IntPtr hWnd = process.MainWindowHandle;
            if (hWnd == IntPtr.Zero) throw new InvalidOperationException("Process has no main window.");

            GetWindowRect(hWnd, out RECT currentRect);
            int width = currentRect.Right - currentRect.Left;
            int height = currentRect.Bottom - currentRect.Top;

            RECT target = monitors[monitorIndex];

            SetWindowPos(hWnd, HWND_TOP, target.Left, target.Top, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }
}
