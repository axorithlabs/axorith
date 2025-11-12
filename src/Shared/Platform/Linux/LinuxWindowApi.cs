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
    public static async Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.Now;
        
        while (!HasWindow(process))
        {
            if ((DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
                throw new TimeoutException($"Process window did not appear within {timeoutMs}ms");

            cancellationToken.ThrowIfCancellationRequested();
            
            await Task.Delay(100, cancellationToken);
        }
    }

    /// <summary>
    ///     Moves a window to a specific monitor by index.
    /// </summary>
    public static void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex)
    {
        // Convert IntPtr to window ID (process ID for xdotool)
        var windowId = windowHandle.ToInt64();
        
        // Get monitor geometry
        var monitors = GetMonitors();
        if (monitorIndex < 0 || monitorIndex >= monitors.Count)
            throw new ArgumentOutOfRangeException(nameof(monitorIndex), 
                $"Monitor index {monitorIndex} is out of range. Available monitors: {monitors.Count}");

        var monitor = monitors[monitorIndex];
        var targetX = monitor.X + 50;
        var targetY = monitor.Y + 50;

        // Use xdotool to move window
        ExecuteCommand("xdotool", $"windowmove {windowId} {targetX} {targetY}");
    }

    /// <summary>
    ///     Checks if process has a window.
    /// </summary>
    private static bool HasWindow(Process process)
    {
        try
        {
            // Try to find window by process ID
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
            // Use xrandr to get monitor information
            var output = ExecuteCommand("xrandr", "--query");
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                // Look for lines like: "HDMI-1 connected 1920x1080+1920+0"
                if (line.Contains(" connected ") && line.Contains("+"))
                {
                    var parts = line.Split(new[] { ' ', '+' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < parts.Length - 2; i++)
                    {
                        if (parts[i].Contains("x") && int.TryParse(parts[i + 1], out int x) && int.TryParse(parts[i + 2], out int y))
                        {
                            var resolution = parts[i].Split('x');
                            if (resolution.Length == 2 && 
                                int.TryParse(resolution[0], out int width) && 
                                int.TryParse(resolution[1], out int height))
                            {
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
                }
            }
        }
        catch
        {
            // Fallback to single monitor
            monitors.Add(new MonitorInfo { X = 0, Y = 0, Width = 1920, Height = 1080 });
        }

        return monitors.Count > 0 ? monitors : new List<MonitorInfo> 
        { 
            new MonitorInfo { X = 0, Y = 0, Width = 1920, Height = 1080 } 
        };
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

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException($"Failed to start {command}");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"{command} failed: {error}");
        }

        return output;
    }

    private class MonitorInfo
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
