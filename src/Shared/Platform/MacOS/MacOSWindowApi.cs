using System.Diagnostics;
using System.Runtime.Versioning;
using Axorith.Shared.Platform;

namespace Axorith.Shared.Platform.MacOS;

[SupportedOSPlatform("macos")]
internal static class MacOsWindowApi
{
    public static Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000,
        CancellationToken cancellationToken = default) =>
        WindowWaiter.WaitAsync(process, HasWindow, timeoutMs, cancellationToken);

    public static void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex)
    {
        // macOS uses display arrangement from System Preferences
        // We'll use AppleScript to move windows between displays

        var script = @"
tell application ""System Events""
    set frontProcess to first process whose frontmost is true
    tell frontProcess
        set position of window 1 to {100, 100}
    end tell
end tell";

        CommandRunner.RunChecked("osascript", ["-e", script]);
    }

    private static bool HasWindow(Process process)
    {
        try
        {
            // Use lsappinfo to check if app has windows
            var output = CommandRunner.RunChecked("lsappinfo", ["info", "-only", "name", process.Id.ToString()]);
            return !string.IsNullOrWhiteSpace(output) && output.Contains('"');
        }
        catch
        {
            return false;
        }
    }

}
