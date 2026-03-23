using System.Runtime.Versioning;
using System.Text.Json;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Linux;

[SupportedOSPlatform("linux")]
internal sealed class LinuxNativeMessagingManager(ILogger logger) : INativeMessagingManager
{
    public void RegisterFirefoxHost(string hostName, string executablePath, string[] allowedExtensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        ValidateExecutablePath(executablePath);

        try
        {
            var manifestDir = GetFirefoxNativeMessagingDirectory();
            Directory.CreateDirectory(manifestDir);

            var manifest = new
            {
                name = hostName,
                description = "Native messaging host for Axorith",
                path = executablePath,
                type = "stdio",
                allowed_extensions = allowedExtensions
            };

            WriteManifest(manifestDir, hostName, manifest);
            logger.LogInformation("Successfully registered Firefox Native Messaging Host '{HostName}'", hostName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register Firefox Native Messaging Host '{HostName}'", hostName);
            throw;
        }
    }

    public void RegisterChromeHost(string hostName, string executablePath, string[] allowedOrigins)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        ValidateExecutablePath(executablePath);

        var browsers = new[]
        {
            ("google-chrome", GetChromeNativeMessagingDirectory("google-chrome")),
            ("chromium", GetChromeNativeMessagingDirectory("chromium")),
            ("microsoft-edge", GetChromeNativeMessagingDirectory("microsoft-edge"))
        };

        var manifest = new
        {
            name = hostName,
            description = "Native messaging host for Axorith",
            path = executablePath,
            type = "stdio",
            allowed_origins = allowedOrigins
        };

        foreach (var (browserName, manifestDir) in browsers)
        {
            try
            {
                Directory.CreateDirectory(manifestDir);
                WriteManifest(manifestDir, hostName, manifest);
                logger.LogDebug("Registered Native Messaging Host for {Browser}", browserName);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to register Native Messaging Host for {Browser}", browserName);
            }
        }

        logger.LogInformation("Successfully registered Chrome/Chromium Native Messaging Host '{HostName}'", hostName);
    }

    private void ValidateExecutablePath(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            logger.LogWarning(
                "Native Messaging Host executable not found at '{Path}'. Registration might be invalid.",
                TelemetryGuard.SafePath(executablePath));
        }

        if (IsSandboxedBrowserDetected())
        {
            logger.LogWarning(
                "Browser appears to be running in a sandboxed environment (Flatpak/Snap). " +
                "Native messaging may not work correctly. " +
                "Consider using Flatseal to grant browser access to Axorith.");
        }
    }

    private static bool IsSandboxedBrowserDetected()
    {
        try
        {
            // Primary check: /.flatpak-info is the reliable detection method
            // for Flatpak sandboxes in cgroup v2 systems (2026 standard)
            if (File.Exists("/.flatpak-info"))
            {
                return true;
            }

            // Fallback: legacy cgroup v1 detection
            if (File.Exists("/proc/self/cgroup"))
            {
                var cgroup = File.ReadAllText("/proc/self/cgroup");
                if (cgroup.Contains("flatpak", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // ignored
        }

        return false;
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

    private static string GetFirefoxNativeMessagingDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".mozilla", "native-messaging-hosts");
    }

    private static string GetChromeNativeMessagingDirectory(string browserName)
    {
        var xdgConfigHome = GetXdgConfigHome();
        return Path.Combine(xdgConfigHome, browserName, "NativeMessagingHosts");
    }

    private static void WriteManifest(string manifestDir, string hostName, object manifest)
    {
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var jsonContent = JsonSerializer.Serialize(manifest, jsonOptions);

        var manifestPath = Path.Combine(manifestDir, $"{hostName}.json");
        File.WriteAllText(manifestPath, jsonContent);
    }
}
