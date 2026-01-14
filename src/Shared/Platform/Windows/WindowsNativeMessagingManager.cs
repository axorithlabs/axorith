using System.Runtime.Versioning;
using System.Text.Json;
using Axorith.Shared.Utils;
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
                executablePath);
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
            logger.LogDebug("Generated Native Messaging manifest at: {Path}", manifestPath);

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

    public void RegisterChromeHost(string hostName, string executablePath, string[] allowedOrigins)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        if (!File.Exists(executablePath))
        {
            logger.LogWarning("Native Messaging Host executable not found at '{Path}'. Registration might be invalid.",
                executablePath);
        }

        try
        {
            var manifestDir = ApplicationPaths.EnsureDirectoryExists(ApplicationPaths.NativeMessagingChrome);

            var manifest = new
            {
                name = hostName,
                description = "Native messaging host for Axorith",
                path = executablePath,
                type = "stdio",
                allowed_origins = allowedOrigins
            };

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            var jsonContent = JsonSerializer.Serialize(manifest, jsonOptions);

            var manifestFileName = $"{hostName}.json";
            var manifestPath = Path.Combine(manifestDir, manifestFileName);

            File.WriteAllText(manifestPath, jsonContent);
            logger.LogDebug("Generated Chrome Native Messaging manifest at: {Path}", manifestPath);

            var chromeRegistryPath = $@"Software\Google\Chrome\NativeMessagingHosts\{hostName}";
            RegisterInRegistry(chromeRegistryPath, manifestPath);

            var chromiumRegistryPath = $@"Software\Chromium\NativeMessagingHosts\{hostName}";
            RegisterInRegistry(chromiumRegistryPath, manifestPath);

            var edgeRegistryPath = $@"Software\Microsoft\Edge\NativeMessagingHosts\{hostName}";
            RegisterInRegistry(edgeRegistryPath, manifestPath);

            logger.LogInformation("Successfully registered Chrome/Chromium Native Messaging Host '{HostName}'",
                hostName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register Chrome Native Messaging Host '{HostName}'", hostName);
            throw;
        }
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