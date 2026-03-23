using Axorith.Shared.Platform;
using Axorith.Telemetry;
using Microsoft.Extensions.Options;

namespace Axorith.Host.Services;

/// <summary>
///     A hosted service that runs once at startup to ensure the Native Messaging Host
///     is correctly registered with the browser.
/// </summary>
public class NativeMessagingRegistrar(
    INativeMessagingManager manager,
    IOptions<Configuration> config,
    ILogger<NativeMessagingRegistrar> logger) : IHostedService
{
    private readonly string _chromeExtensionId = config.Value.BrowserExtensions.ChromeExtensionId;
    private readonly string _firefoxExtensionId = config.Value.BrowserExtensions.FirefoxExtensionId;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            RegisterHost();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to register Native Messaging Host. Site Blocker functionality may be unavailable.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void RegisterHost()
    {
        #if DEBUG
        var hostName = "axorith.dev";
        #else
        var hostName = "axorith";
        #endif

        logger.LogInformation("Registering Native Messaging Host as '{HostName}'",
            hostName);

        var baseDir = AppContext.BaseDirectory;
        var shimPath = Path.GetFullPath(Path.Combine(baseDir, "..", "Axorith.Shim", "Axorith.Shim.exe"));

        if (!File.Exists(shimPath))
        {
            logger.LogWarning("Axorith.Shim.exe not found at expected path: {Path}. Skipping registration.",
                TelemetryGuard.SafePath(shimPath));
            return;
        }

        logger.LogInformation("Found Shim executable at: {Path}", TelemetryGuard.SafePath(shimPath));

        manager.RegisterFirefoxHost(hostName, shimPath, [_firefoxExtensionId]);

        #if DEBUG
        logger.LogWarning(
            "DEBUG MODE: Allowing wildcard chrome-extension origins for development. " +
            "This is insecure and must be replaced with actual extension ID in production.");
        manager.RegisterChromeHost(hostName, shimPath, ["chrome-extension://*/*"]);
        #else
        if (string.IsNullOrEmpty(_chromeExtensionId))
        {
            logger.LogError(
                "Chrome extension ID not configured. Native Messaging for Chrome will not work. " +
                "Set BrowserExtensions:ChromeExtensionId in appsettings.json or environment variable.");
        }
        else
        {
            var chromeOrigin = $"chrome-extension://{_chromeExtensionId}/";
            manager.RegisterChromeHost(hostName, shimPath, [chromeOrigin]);
            logger.LogInformation("Registered Chrome Native Messaging Host with extension ID: {ExtensionId}",
                _chromeExtensionId);
        }
        #endif
    }
}