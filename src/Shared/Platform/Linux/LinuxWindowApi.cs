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
    private static bool HasWindow(Process process)
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
}