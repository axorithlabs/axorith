using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Axorith.Shared.Platform.Windows;

/// <summary>
///     Windows-specific window management API.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowApi
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;

    /// <summary>
    ///     Waits for a process to create its main window handle.
    /// </summary>
    public static async Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.Now;
        
        while (process.MainWindowHandle == IntPtr.Zero)
        {
            if ((DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
                throw new TimeoutException($"Process window did not appear within {timeoutMs}ms");

            cancellationToken.ThrowIfCancellationRequested();
            
            process.Refresh();
            await Task.Delay(100, cancellationToken);
        }
    }

    /// <summary>
    ///     Moves a window to a specific monitor by index.
    /// </summary>
    public static void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex)
    {
        var monitors = new List<Rect>();
        
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData) =>
            {
                monitors.Add(lprcMonitor);
                return true;
            }, IntPtr.Zero);

        if (monitorIndex < 0 || monitorIndex >= monitors.Count)
            throw new ArgumentOutOfRangeException(nameof(monitorIndex), 
                $"Monitor index {monitorIndex} is out of range. Available monitors: {monitors.Count}");

        var targetMonitor = monitors[monitorIndex];
        var targetX = targetMonitor.Left + 50;
        var targetY = targetMonitor.Top + 50;

        SetWindowPos(windowHandle, IntPtr.Zero, targetX, targetY, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
    }
}
