using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Axorith.Shared.Platform.Windows;

/// <summary>
///     Windows window states.
/// </summary>
public enum WindowState
{
    Normal = 1,
    Minimized = 2,
    Maximized = 3,
    Hidden = 0
}

/// <summary>
///     Windows-specific window management API.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowApi
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool
        EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice,
        uint dwFlags);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    private const uint SwpNosize = 0x0001;
    private const uint SwpNozorder = 0x0004;
    private const uint SwpNomove = 0x0002;

    private const int SwHide = 0;
    private const int SwShownormal = 1;
    private const int SwShowminimized = 2;
    private const int SwShowmaximized = 3;
    private const int SwRestore = 9;

    private const int DisplayDeviceActive = 0x00000001;

    /// <summary>
    ///     Waits for a process to create its main window handle.
    /// </summary>
    public static async Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
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
            (_, _, ref lprcMonitor, _) =>
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

        SetWindowPos(windowHandle, IntPtr.Zero, targetX, targetY, 0, 0, SwpNosize | SwpNozorder);
    }

    /// <summary>
    ///     Finds processes by name or executable path.
    /// </summary>
    public static List<Process> FindProcesses(string processNameOrPath)
    {
        var results = new List<Process>();

        // Try exact process name match
        var processName = Path.GetFileNameWithoutExtension(processNameOrPath);
        results.AddRange(Process.GetProcessesByName(processName));

        // Also try by executable path
        if (!File.Exists(processNameOrPath)) return results;

        var allProcesses = Process.GetProcesses();
        foreach (var process in allProcesses)
            try
            {
                if (process.MainModule?.FileName.Equals(processNameOrPath, StringComparison.OrdinalIgnoreCase) !=
                    true) continue;
                if (results.All(p => p.Id != process.Id))
                    results.Add(process);
            }
            catch
            {
                // Process may not be accessible, skip
            }

        return results;
    }

    /// <summary>
    ///     Sets window state (Normal, Minimized, Maximized).
    /// </summary>
    public static void SetWindowState(IntPtr windowHandle, WindowState state)
    {
        var cmdShow = state switch
        {
            WindowState.Normal => SwShownormal,
            WindowState.Minimized => SwShowminimized,
            WindowState.Maximized => SwShowmaximized,
            WindowState.Hidden => SwHide,
            _ => SwShownormal
        };

        ShowWindow(windowHandle, cmdShow);
    }

    /// <summary>
    ///     Gets current window state.
    /// </summary>
    public static WindowState GetWindowState(IntPtr windowHandle)
    {
        if (IsIconic(windowHandle))
            return WindowState.Minimized;
        if (IsZoomed(windowHandle))
            return WindowState.Maximized;
        return WindowState.Normal;
    }

    /// <summary>
    ///     Sets window size.
    /// </summary>
    public static void SetWindowSize(IntPtr windowHandle, int width, int height)
    {
        SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, width, height, SwpNomove | SwpNozorder);
    }

    /// <summary>
    ///     Sets window position.
    /// </summary>
    public static void SetWindowPosition(IntPtr windowHandle, int x, int y)
    {
        SetWindowPos(windowHandle, IntPtr.Zero, x, y, 0, 0, SwpNosize | SwpNozorder);
    }

    /// <summary>
    ///     Gets window size and position.
    /// </summary>
    public static (int X, int Y, int Width, int Height) GetWindowBounds(IntPtr windowHandle)
    {
        GetWindowRect(windowHandle, out var rect);
        return (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>
    ///     Brings window to foreground.
    /// </summary>
    public static void FocusWindow(IntPtr windowHandle)
    {
        if (IsIconic(windowHandle))
            ShowWindow(windowHandle, SwRestore);
        else if (IsZoomed(windowHandle)) ShowWindow(windowHandle, SwShowmaximized);
        SetForegroundWindow(windowHandle);
    }

    /// <summary>
    ///     Gets monitor count.
    /// </summary>
    public static int GetMonitorCount()
    {
        var monitors = new List<Rect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (_, _, ref lprcMonitor, _) =>
            {
                monitors.Add(lprcMonitor);
                return true;
            }, IntPtr.Zero);
        return monitors.Count;
    }

    public static (int X, int Y, int Width, int Height) GetMonitorBounds(int monitorIndex)
    {
        var monitors = new List<Rect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (_, _, ref lprcMonitor, _) =>
            {
                monitors.Add(lprcMonitor);
                return true;
            }, IntPtr.Zero);

        if (monitorIndex < 0 || monitorIndex >= monitors.Count)
            throw new ArgumentOutOfRangeException(nameof(monitorIndex),
                $"Monitor index {monitorIndex} is out of range. Available monitors: {monitors.Count}");

        var target = monitors[monitorIndex];
        return (target.Left, target.Top, target.Right - target.Left, target.Bottom - target.Top);
    }

    public static string GetMonitorName(int monitorIndex)
    {
        var dd = new DisplayDevice();
        dd.cb = Marshal.SizeOf<DisplayDevice>();

        var foundIndex = 0;
        uint devNum = 0;

        while (EnumDisplayDevices(null, devNum, ref dd, 0))
        {
            var isActive = (dd.StateFlags & DisplayDeviceActive) != 0;

            if (isActive)
            {
                if (foundIndex == monitorIndex)
                    return string.IsNullOrWhiteSpace(dd.DeviceString) ? dd.DeviceName : dd.DeviceString;

                foundIndex++;
            }

            devNum++;
            dd = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
        }

        return $"Monitor {monitorIndex + 1}";
    }
}