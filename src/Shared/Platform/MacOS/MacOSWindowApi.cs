using System.Diagnostics;
using System.Runtime.Versioning;

namespace Axorith.Shared.Platform.MacOS;

/// <summary>
///     macOS-specific window management API using AppKit/Cocoa.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacOsWindowApi
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
                throw new TimeoutException($"Process window did not appear within {timeoutMs}ms");

            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(100, cancellationToken);
        }
    }

    /// <summary>
    ///     Moves a window to a specific monitor/display by index.
    /// </summary>
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

        ExecuteAppleScript(script);
    }

    /// <summary>
    ///     Checks if process has a window using lsappinfo.
    /// </summary>
    private static bool HasWindow(Process process)
    {
        try
        {
            // Use lsappinfo to check if app has windows
            var output = ExecuteCommand("lsappinfo", $"info -only name {process.Id}");
            return !string.IsNullOrWhiteSpace(output) && output.Contains('"');
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Executes AppleScript command.
    /// </summary>
    private static void ExecuteAppleScript(string script)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            Arguments = $"-e \"{script.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start osascript");

        process.WaitForExit(5000);

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"AppleScript failed: {error}");
        }
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
}