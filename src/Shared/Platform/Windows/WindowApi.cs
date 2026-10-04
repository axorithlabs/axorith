using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Axorith.Shared.Platform;

namespace Axorith.Shared.Platform.Windows;

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

    public static Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000,
        CancellationToken cancellationToken = default) =>
        WindowWaiter.WaitAsync(process, HasWindow, timeoutMs, cancellationToken);

    private static bool HasWindow(Process process)
    {
        process.Refresh();
        return process.MainWindowHandle != IntPtr.Zero;
    }

    public static void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex)
    {
        var targetMonitor = GetMonitor(monitorIndex);
        var targetX = targetMonitor.Left + 50;
        var targetY = targetMonitor.Top + 50;

        SetWindowPos(windowHandle, IntPtr.Zero, targetX, targetY, 0, 0, SwpNosize | SwpNozorder);
    }

    private static List<Rect> GetMonitors()
    {
        var monitors = new List<Rect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (_, _, ref rect, _) =>
            {
                monitors.Add(rect);
                return true;
            }, IntPtr.Zero);
        return monitors;
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

    public static void SetWindowSize(IntPtr windowHandle, int width, int height) => SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, width, height, SwpNomove | SwpNozorder);

    public static void SetWindowPosition(IntPtr windowHandle, int x, int y) => SetWindowPos(windowHandle, IntPtr.Zero, x, y, 0, 0, SwpNosize | SwpNozorder);

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

    public static int GetMonitorCount() => GetMonitors().Count;

    public static (int X, int Y, int Width, int Height) GetMonitorBounds(int monitorIndex)
    {
        var target = GetMonitor(monitorIndex);
        return (target.Left, target.Top, target.Right - target.Left, target.Bottom - target.Top);
    }

    private static Rect GetMonitor(int monitorIndex)
    {
        var monitors = GetMonitors();
        if (monitorIndex < 0 || monitorIndex >= monitors.Count)
            throw new ArgumentOutOfRangeException(nameof(monitorIndex),
                $"Monitor index {monitorIndex} is out of range. Available monitors: {monitors.Count}");

        return monitors[monitorIndex];
    }

    public static string GetMonitorName(int monitorIndex)
    {
        try
        {
            var monitorNames = GetMonitorNamesFromEdid();
            if (monitorIndex >= 0 && monitorIndex < monitorNames.Count)
            {
                return monitorNames[monitorIndex];
            }
        }
        catch
        {
            // Fall through to fallback
        }

        return GetMonitorNameFallback(monitorIndex);
    }

    private static List<string> GetMonitorNamesFromEdid()
    {
        var names = new List<string>();

        using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorID");
        foreach (var obj in searcher.Get())
        {
            var userFriendlyName = DecodeUshortArray(obj["UserFriendlyName"] as ushort[]);
            if (!string.IsNullOrWhiteSpace(userFriendlyName))
            {
                names.Add(userFriendlyName);
                continue;
            }

            var manufacturerName = obj["ManufacturerName"] as ushort[];
            var productCodeId = obj["ProductCodeID"] as ushort[];

            var manufacturer = DecodeUshortArray(manufacturerName);
            var productCode = DecodeUshortArray(productCodeId);

            if (!string.IsNullOrWhiteSpace(manufacturer) || !string.IsNullOrWhiteSpace(productCode))
            {
                names.Add($"{manufacturer} {productCode}".Trim());
            }
            else
            {
                names.Add($"Monitor {names.Count + 1}");
            }
        }

        return names;
    }

    private static string DecodeUshortArray(ushort[]? arr)
    {
        if (arr == null || arr.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var c in arr)
        {
            if (c == 0)
            {
                break;
            }

            sb.Append((char)c);
        }

        return sb.ToString().Trim();
    }

    private static string GetMonitorNameFallback(int monitorIndex)
    {
        var adapter = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
        var monitor = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };

        var foundIndex = 0;
        uint adapterNum = 0;

        while (EnumDisplayDevices(null, adapterNum, ref adapter, 0))
        {
            var isActive = (adapter.StateFlags & DisplayDeviceActive) != 0;

            if (isActive)
            {
                if (foundIndex == monitorIndex)
                {
                    if (EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0))
                    {
                        if (!string.IsNullOrWhiteSpace(monitor.DeviceString) &&
                            !monitor.DeviceString.Contains("Generic", StringComparison.OrdinalIgnoreCase))
                        {
                            return monitor.DeviceString;
                        }
                    }

                    return $"Monitor {monitorIndex + 1}";
                }

                foundIndex++;
            }

            adapterNum++;
            adapter = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
        }

        return $"Monitor {monitorIndex + 1}";
    }
}
