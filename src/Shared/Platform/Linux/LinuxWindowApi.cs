using System.Diagnostics;
using System.Runtime.Versioning;
using Axorith.Shared.Platform;

namespace Axorith.Shared.Platform.Linux;

[SupportedOSPlatform("linux")]
internal static class LinuxWindowApi
{
    public static Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000,
        CancellationToken cancellationToken = default) =>
        WindowWaiter.WaitAsync(process, HasWindow, timeoutMs, cancellationToken);

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

        CommandRunner.RunChecked("xdotool",
            ["windowmove", windowId.ToString(), targetX.ToString(), targetY.ToString()]);
    }

    private static bool HasWindow(Process process)
    {
        try
        {
            var output = CommandRunner.RunChecked("xdotool", ["search", "--pid", process.Id.ToString()]);
            return !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }

    private static List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();

        try
        {
            var output = CommandRunner.RunChecked("xrandr", ["--query"]);
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
                        !int.TryParse(resolution[0], out _) ||
                        !int.TryParse(resolution[1], out _))
                    {
                        continue;
                    }

                    monitors.Add(new MonitorInfo { X = x, Y = y });
                    break;
                }
            }
        }
        catch
        {
            // Fallback to single monitor
            monitors.Add(new MonitorInfo());
        }

        return monitors.Count > 0 ? monitors : [new MonitorInfo()];
    }


    private class MonitorInfo
    {
        public int X { get; init; }
        public int Y { get; init; }
    }
}
