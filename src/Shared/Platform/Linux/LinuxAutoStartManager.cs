using System.Runtime.Versioning;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Linux;

[SupportedOSPlatform("linux")]
internal sealed class LinuxAutoStartManager(ILogger logger) : IAutoStartManager
{
    private const string DesktopFileName = "axorith.desktop";

    public bool IsAutoStartEnabled
    {
        get
        {
            var autostartPath = GetAutostartFilePath();
            var exists = File.Exists(autostartPath);

            if (exists)
            {
                logger.LogDebug("Auto-start file found at: {Path}", TelemetryGuard.SafePath(autostartPath));
            }

            return exists;
        }
    }

    public bool IsStartMinimized
    {
        get
        {
            var autostartPath = GetAutostartFilePath();
            if (!File.Exists(autostartPath))
            {
                return false;
            }

            try
            {
                var content = File.ReadAllText(autostartPath);
                return content.Contains("--tray", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read autostart file to check minimized status");
                return false;
            }
        }
    }

    public bool EnableAutoStart(bool startMinimized = true)
    {
        try
        {
            var autostartDir = GetAutostartDirectory();
            Directory.CreateDirectory(autostartDir);

            var autostartPath = Path.Combine(autostartDir, DesktopFileName);
            var execPath = GetExecutablePath();
            var execArgs = startMinimized ? "--tray" : string.Empty;

            var content = $"""
                [Desktop Entry]
                Type=Application
                Name=Axorith
                Exec={execPath}{(!string.IsNullOrEmpty(execArgs) ? $" {execArgs}" : "")}
                Hidden=false
                X-GNOME-Autostart-enabled=true
                """;

            File.WriteAllText(autostartPath, content);
            SetFilePermissions(autostartPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            logger.LogInformation("Auto-start enabled (minimized: {Minimized})", startMinimized);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enable auto-start");
            return false;
        }
    }

    public bool DisableAutoStart()
    {
        try
        {
            var autostartPath = GetAutostartFilePath();

            if (File.Exists(autostartPath))
            {
                File.Delete(autostartPath);
                logger.LogInformation("Auto-start disabled");
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to disable auto-start");
            return false;
        }
    }

    private static string GetXdgConfigHome()
    {
        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdgConfigHome))
        {
            return xdgConfigHome;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config");
    }

    private static string GetAutostartDirectory()
    {
        return Path.Combine(GetXdgConfigHome(), "autostart");
    }

    private static string GetAutostartFilePath()
    {
        return Path.Combine(GetAutostartDirectory(), DesktopFileName);
    }

    private static string GetExecutablePath()
    {
        var exePath = Environment.ProcessPath;
        return !string.IsNullOrEmpty(exePath) ? exePath : "axorith";
    }

    private static void SetFilePermissions(string path, UnixFileMode mode)
    {
        try
        {
            var octal = Convert.ToString((int)mode, 8);
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"{octal} \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(1000);
        }
        catch
        {
            // ignored
        }
    }
}
