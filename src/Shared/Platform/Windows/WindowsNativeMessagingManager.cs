using System.Runtime.Versioning;
using System.Text.Json;
using Axorith.Shared.Utils;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Axorith.Shared.Platform.Windows;

[SupportedOSPlatform("windows")]
internal class WindowsNativeMessagingManager(ILogger<WindowsNativeMessagingManager> logger) : INativeMessagingManager
{
    public void RegisterFirefoxHost(string hostName, string executablePath, string[] allowedExtensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        if (!File.Exists(executablePath))
        {
            logger.LogWarning("Native Messaging Host executable not found at '{Path}'. Registration might be invalid.",
                TelemetryGuard.SafePath(executablePath));
        }

        try
        {
            var manifestDir = ApplicationPaths.EnsureDirectoryExists(ApplicationPaths.NativeMessagingFirefox);

            var manifest = new
            {
                name = hostName,
                description = "Native messaging host for Axorith",
                path = executablePath,
                type = "stdio",
                args = new[] { "--browser=firefox" },
                allowed_extensions = allowedExtensions
            };

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            var jsonContent = JsonSerializer.Serialize(manifest, jsonOptions);

            var manifestFileName = $"{hostName}.json";
            var manifestPath = Path.Combine(manifestDir, manifestFileName);

            File.WriteAllText(manifestPath, jsonContent);
            logger.LogDebug("Generated Native Messaging manifest at: {Path}", TelemetryGuard.SafePath(manifestPath));

            var registryPath = $@"Software\Mozilla\NativeMessagingHosts\{hostName}";

            using var key = Registry.CurrentUser.CreateSubKey(registryPath, writable: true);
            if (key == null)
            {
                throw new InvalidOperationException($"Failed to create or open registry key: {registryPath}");
            }

            key.SetValue(string.Empty, manifestPath, RegistryValueKind.String);

            logger.LogInformation("Successfully registered Firefox Native Messaging Host '{HostName}'", hostName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register Native Messaging Host '{HostName}'", hostName);
            throw;
        }
    }

    public void RegisterChromeHost(string hostName, string executablePath,
        IReadOnlyDictionary<string, string[]> allowedOriginsByBrowser)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        if (!File.Exists(executablePath))
        {
            logger.LogWarning("Native Messaging Host executable not found at '{Path}'. Registration might be invalid.",
                TelemetryGuard.SafePath(executablePath));
        }

        try
        {
            var manifestDir = ApplicationPaths.EnsureDirectoryExists(ApplicationPaths.NativeMessagingChrome);

            var chromeManifest = WriteChromeManifest(manifestDir, hostName, executablePath,
                allowedOriginsByBrowser.GetValueOrDefault("chrome", []), "chrome");
            var edgeManifest = WriteChromeManifest(manifestDir, hostName, executablePath,
                allowedOriginsByBrowser.GetValueOrDefault("edge", []), "edge");
            var chromiumManifest = WriteChromeManifest(manifestDir, hostName, executablePath,
                allowedOriginsByBrowser.GetValueOrDefault("chromium", []), "chromium");

            RegisterInRegistry($@"Software\Google\Chrome\NativeMessagingHosts\{hostName}", chromeManifest);
            RegisterInRegistry($@"Software\Microsoft\Edge\NativeMessagingHosts\{hostName}", edgeManifest);
            RegisterInRegistry($@"Software\Chromium\NativeMessagingHosts\{hostName}", chromiumManifest);

            logger.LogInformation("Successfully registered Chrome/Chromium Native Messaging Host '{HostName}'",
                hostName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register Chrome Native Messaging Host '{HostName}'", hostName);
            throw;
        }
    }

    private string WriteChromeManifest(string manifestDir, string hostName, string executablePath,
        string[] allowedOrigins, string browser)
    {
        var manifest = new
        {
            name = hostName,
            description = "Native messaging host for Axorith",
            path = executablePath,
            type = "stdio",
            args = new[] { $"--browser={browser}" },
            allowed_origins = allowedOrigins
        };

        var manifestPath = Path.Combine(manifestDir, $"{hostName}.{browser}.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        logger.LogDebug("Generated {Browser} Native Messaging Host manifest at: {Path}", browser,
            TelemetryGuard.SafePath(manifestPath));
        return manifestPath;
    }

    private void RegisterInRegistry(string registryPath, string manifestPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(registryPath, writable: true);
        if (key == null)
        {
            logger.LogWarning("Failed to create or open registry key: {RegistryPath}", registryPath);
            return;
        }

        key.SetValue(string.Empty, manifestPath, RegistryValueKind.String);
        logger.LogDebug("Registered Native Messaging Host in registry: {RegistryPath}", registryPath);
    }
}
