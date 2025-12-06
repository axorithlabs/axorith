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
    public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public const uint WINEVENT_OUTOFCONTEXT = 0;
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_OBJECT_CREATE = 0x8000;

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

    public static async Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.Now;

        while (process.MainWindowHandle == IntPtr.Zero)
        {
            if ((DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
            {
                throw new TimeoutException($"Process window did not appear within {timeoutMs}ms");
            }

            cancellationToken.ThrowIfCancellationRequested();

            process.Refresh();
            await Task.Delay(100, cancellationToken);
        }
    }

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
        {
            throw new ArgumentOutOfRangeException(nameof(monitorIndex),
                $"Monitor index {monitorIndex} is out of range. Available monitors: {monitors.Count}");
        }

        var targetMonitor = monitors[monitorIndex];
        var targetX = targetMonitor.Left + 50;
        var targetY = targetMonitor.Top + 50;

        SetWindowPos(windowHandle, IntPtr.Zero, targetX, targetY, 0, 0, SwpNosize | SwpNozorder);
    }

    public static List<Process> FindProcesses(string processNameOrPath)
    {
        var results = new List<Process>();

        var processName = Path.GetFileNameWithoutExtension(processNameOrPath);
        results.AddRange(Process.GetProcessesByName(processName));

        if (!File.Exists(processNameOrPath))
        {
            return results;
        }

        var allProcesses = Process.GetProcesses();
        foreach (var process in allProcesses)
        {
            try
            {
                if (process.MainModule?.FileName.Equals(processNameOrPath, StringComparison.OrdinalIgnoreCase) !=
                    true)
                {
                    continue;
                }

                if (results.All(p => p.Id != process.Id))
                {
                    results.Add(process);
                }
            }
            catch
            {
                // Process may not be accessible, skip
            }
        }

        return results;
    }

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

    public static WindowState GetWindowState(IntPtr windowHandle)
    {
        if (IsIconic(windowHandle))
        {
            return WindowState.Minimized;
        }

        if (IsZoomed(windowHandle))
        {
            return WindowState.Maximized;
        }

        return WindowState.Normal;
    }

    public static void SetWindowSize(IntPtr windowHandle, int width, int height)
    {
        SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, width, height, SwpNomove | SwpNozorder);
    }

    public static void SetWindowPosition(IntPtr windowHandle, int x, int y)
    {
        SetWindowPos(windowHandle, IntPtr.Zero, x, y, 0, 0, SwpNosize | SwpNozorder);
    }

    public static (int X, int Y, int Width, int Height) GetWindowBounds(IntPtr windowHandle)
    {
        GetWindowRect(windowHandle, out var rect);
        return (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public static void FocusWindow(IntPtr windowHandle)
    {
        if (IsIconic(windowHandle))
        {
            ShowWindow(windowHandle, SwRestore);
        }
        else if (IsZoomed(windowHandle))
        {
            ShowWindow(windowHandle, SwShowmaximized);
        }

        SetForegroundWindow(windowHandle);
    }

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
        {
            throw new ArgumentOutOfRangeException(nameof(monitorIndex),
                $"Monitor index {monitorIndex} is out of range. Available monitors: {monitors.Count}");
        }

        var target = monitors[monitorIndex];
        return (target.Left, target.Top, target.Right - target.Left, target.Bottom - target.Top);
    }

    public static string GetMonitorName(int monitorIndex)
    {
        var dd = new DisplayDevice
        {
            cb = Marshal.SizeOf<DisplayDevice>()
        };

        var foundIndex = 0;
        uint devNum = 0;

        while (EnumDisplayDevices(null, devNum, ref dd, 0))
        {
            var isActive = (dd.StateFlags & DisplayDeviceActive) != 0;

            if (isActive)
            {
                if (foundIndex == monitorIndex)
                {
                    return string.IsNullOrWhiteSpace(dd.DeviceString) ? dd.DeviceName : dd.DeviceString;
                }

                foundIndex++;
            }

            devNum++;
            dd = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
        }

        return $"Monitor {monitorIndex + 1}";
    }
}