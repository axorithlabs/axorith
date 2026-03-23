using System.Diagnostics;
using System.Runtime.Versioning;

namespace Axorith.Shared.Platform.Linux;

/// <summary>
///     Linux-specific window management API using X11.
///     Supports both X11 and Wayland through xdotool.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class LinuxWindowApi
{
    private const string WaylandDisplayEnv = "WAYLAND_DISPLAY";
    private const string XdgSessionTypeEnv = "XDG_SESSION_TYPE";

    /// <summary>
    ///     Gets whether the current Linux session uses Wayland instead of X11.
    /// </summary>
    public static bool IsWaylandSession()
    {
        var sessionType = Environment.GetEnvironmentVariable(XdgSessionTypeEnv);
        if (string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var waylandDisplay = Environment.GetEnvironmentVariable(WaylandDisplayEnv);
        return !string.IsNullOrEmpty(waylandDisplay);
    }

    /// <summary>
    ///     Gets a user-facing message explaining why window management is unavailable.
    ///     Returns null if window management should work normally.
    /// </summary>
    public static string? GetWindowManagementStatusMessage()
    {
        if (IsWaylandSession())
        {
            return "Window management (move/resize/minimize) is unavailable on Wayland sessions. " +
                   "Switch to X11/XWayland for full window control.";
        }

        return null;
    }

    /// <summary>
    ///     Waits for a process to create its main window.
    /// </summary>
    public static async Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.Now;

        while (!HasWindow(process))
        {
            if ((DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
            {
                throw new TimeoutException($"Process window did not appear within {timeoutMs}ms");
            }

            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(100, cancellationToken);
        }
    }

    /// <summary>
    ///     Moves a window to a specific monitor by index.
    /// </summary>
    public static void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex)
    {
        if (IsWaylandSession())
        {
            return; // Silent failure — GetWindowManagementStatusMessage() provides UI visibility
        }

        var windowId = windowHandle.ToInt64();

        var monitors = GetMonitors();
        if (monitorIndex < 0 || monitorIndex >= monitors.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(monitorIndex),
                $"Monitor index {monitorIndex} is out of range. Available monitors: {monitors.Count}");
        }

        var monitor = monitors[monitorIndex];
        var targetX = monitor.X + 50;
        var targetY = monitor.Y + 50;

        ExecuteCommand("xdotool", $"windowmove {windowId} {targetX} {targetY}");
    }

    /// <summary>
    ///     Checks if process has a window.
    /// </summary>
    internal static bool HasWindow(Process process)
    {
        try
        {
            var output = ExecuteCommand("xdotool", $"search --pid {process.Id}");
            return !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Checks if a process with given PID has a window.
    /// </summary>
    internal static bool HasWindow(int processId)
    {
        try
        {
            var output = ExecuteCommand("xdotool", $"search --pid {processId}");
            return !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Gets list of available monitors.
    /// </summary>
    private static List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();

        try
        {
            var output = ExecuteCommand("xrandr", "--query");
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (!line.Contains(" connected ") || !line.Contains('+'))
                {
                    continue;
                }

                var parts = line.Split([' ', '+'], StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < parts.Length - 2; i++)
                {
                    if (!parts[i].Contains('x') || !int.TryParse(parts[i + 1], out var x) ||
                        !int.TryParse(parts[i + 2], out var y))
                    {
                        continue;
                    }

                    var resolution = parts[i].Split('x');
                    if (resolution.Length != 2 ||
                        !int.TryParse(resolution[0], out var width) ||
                        !int.TryParse(resolution[1], out var height))
                    {
                        continue;
                    }

                    monitors.Add(new MonitorInfo
                    {
                        X = x,
                        Y = y,
                        Width = width,
                        Height = height
                    });
                    break;
                }
            }
        }
        catch
        {
            // Fallback to single monitor
            monitors.Add(new MonitorInfo { X = 0, Y = 0, Width = 1920, Height = 1080 });
        }

        return monitors.Count > 0 ? monitors : [new MonitorInfo { X = 0, Y = 0, Width = 1920, Height = 1080 }];
    }

    /// <summary>
    ///     Executes a shell command and returns output.
    /// </summary>
    private static string ExecuteCommand(string command, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {command}");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);

        if (process.ExitCode == 0)
        {
            return output;
        }

        var error = process.StandardError.ReadToEnd();
        throw new InvalidOperationException($"{command} failed: {error}");
    }

    private class MonitorInfo
    {
        public int X { get; init; }
        public int Y { get; init; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    // Stub implementations for IPlatformWindowService — return defaults on Wayland

    public static void SetWindowState(IntPtr windowHandle, WindowState state)
    {
        if (IsWaylandSession()) return;
        ExecuteCommand("xdotool", $"windowstate {'_' + state.ToString().ToLower()} {windowHandle.ToInt64()}");
    }

    public static WindowState GetWindowState(IntPtr windowHandle)
    {
        if (IsWaylandSession()) return WindowState.Normal;
        return WindowState.Normal; // xdotool doesn't provide a direct query for state
    }

    public static void SetWindowSize(IntPtr windowHandle, int width, int height)
    {
        if (IsWaylandSession()) return;
        ExecuteCommand("xdotool", $"windowsize {windowHandle.ToInt64()} {width} {height}");
    }

    public static void SetWindowPosition(IntPtr windowHandle, int x, int y)
    {
        if (IsWaylandSession()) return;
        ExecuteCommand("xdotool", $"windowmove {windowHandle.ToInt64()} {x} {y}");
    }

    public static (int X, int Y, int Width, int Height) GetWindowBounds(IntPtr windowHandle)
    {
        if (IsWaylandSession()) return (0, 0, 0, 0);
        var output = ExecuteCommand("xdotool", $"getwindowgeometry --shell {windowHandle.ToInt64()}");
        return ParseGeometry(output);
    }

    public static void FocusWindow(IntPtr windowHandle)
    {
        if (IsWaylandSession()) return;
        ExecuteCommand("xdotool", $"windowactivate {windowHandle.ToInt64()}");
    }

    public static int GetMonitorCount()
    {
        return IsWaylandSession() ? 1 : GetMonitors().Count;
    }

    public static (int X, int Y, int Width, int Height) GetMonitorBounds(int monitorIndex)
    {
        if (IsWaylandSession()) return (0, 0, 1920, 1080);
        var monitors = GetMonitors();
        if (monitorIndex < 0 || monitorIndex >= monitors.Count) return (0, 0, 1920, 1080);
        var m = monitors[monitorIndex];
        return (m.X, m.Y, m.Width, m.Height);
    }

    public static string GetMonitorName(int monitorIndex)
    {
        return $"Monitor {monitorIndex}";
    }

    private static (int X, int Y, int Width, int Height) ParseGeometry(string output)
    {
        int x = 0, y = 0, w = 0, h = 0;
        foreach (var line in output.Split('\n'))
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue;
            switch (parts[0].Trim())
            {
                case "X": int.TryParse(parts[1].Trim(), out x); break;
                case "Y": int.TryParse(parts[1].Trim(), out y); break;
                case "WIDTH": int.TryParse(parts[1].Trim(), out w); break;
                case "HEIGHT": int.TryParse(parts[1].Trim(), out h); break;
            }
        }

        return (x, y, w, h);
    }
}