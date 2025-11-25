using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Axorith.Shared.Platform.Windows;

/// <summary>
///     Windows-specific implementation of INativeMessagingManager.
///     Registers the host via the Windows Registry (HKCU) and creates the manifest file in AppData.
/// </summary>
[SupportedOSPlatform("windows")]
internal class WindowsNativeMessagingManager(ILogger<WindowsNativeMessagingManager> logger) : INativeMessagingManager
{
    public void RegisterFirefoxHost(string hostName, string executablePath, string[] allowedExtensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        if (!File.Exists(executablePath))
        {
            logger.LogWarning("Native Messaging Host executable not found at '{Path}'. Registration might be invalid.", executablePath);
        }

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var manifestDir = Path.Combine(appData, "Axorith", "native-messaging", "firefox");
            
            if (!Directory.Exists(manifestDir))
            {
                Directory.CreateDirectory(manifestDir);
            }

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
}