using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    public void RemoveFirefoxExtensionPolicy(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        RemoveExtensionSettingsEntry(@"Software\Policies\Mozilla\Firefox", "ExtensionSettings", extensionId);
        RemoveExtensionSettingsEntry(@"Software\Policies\Mozilla\Firefox\ExtensionSettings", string.Empty,
            extensionId);
    }

    private void RemoveExtensionSettingsEntry(string registryPath, string valueName, string extensionId)
    {
        using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true);
        if (key == null)
        {
            return;
        }

        var existingValue = key.GetValue(valueName);
        string[] values = existingValue switch
        {
            string[] strings => strings,
            string text => [text],
            _ => []
        };
        var updatedValues = new List<string>(values.Length);
        var changed = false;

        foreach (var value in values)
        {
            try
            {
                if (JsonNode.Parse(value) is not JsonObject settings || !settings.Remove(extensionId))
                {
                    updatedValues.Add(value);
                    continue;
                }

                changed = true;
                if (settings.Count > 0)
                {
                    updatedValues.Add(settings.ToJsonString());
                }
            }
            catch (JsonException)
            {
                updatedValues.Add(value);
            }
        }

        if (!changed)
        {
            return;
        }

        if (updatedValues.Count == 0)
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
        else if (existingValue is string[])
        {
            key.SetValue(valueName, updatedValues.ToArray(), RegistryValueKind.MultiString);
        }
        else
        {
            key.SetValue(valueName, updatedValues[0], RegistryValueKind.String);
        }

        logger.LogInformation("Removed legacy Firefox install policy for add-on {ExtensionId}", extensionId);
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
