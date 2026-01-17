using Axorith.Shared.Platform;
using Axorith.Telemetry;

namespace Axorith.Host.Services;

/// <summary>
///     A hosted service that runs once at startup to ensure the Native Messaging Host
///     is correctly registered with the browser.
/// </summary>
public class NativeMessagingRegistrar(
    INativeMessagingManager manager,
    ILogger<NativeMessagingRegistrar> logger) : IHostedService
{
    private const string FirefoxExtensionId = "site-blocker-firefox@axorithlabs.com";

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
            logger.LogWarning("Axorith.Shim.exe not found at expected path: {Path}. Skipping registration.", TelemetryGuard.SafePath(shimPath));
            return;
        }

        logger.LogInformation("Found Shim executable at: {Path}", TelemetryGuard.SafePath(shimPath));

        // Register for Firefox
        manager.RegisterFirefoxHost(hostName, shimPath, [FirefoxExtensionId]);

        // Register for Chrome/Chromium-based browsers
        // Note: The extension ID will be determined when the extension is loaded in Chrome.
        // For unpacked extensions, Chrome generates an ID based on the extension's path.
        // For published extensions, the ID is fixed by Chrome Web Store.
        // Using wildcard pattern to allow any extension origin during development.
        // In production, replace with the actual Chrome Web Store extension ID.
        #if DEBUG
        // Allow all extensions during development
        manager.RegisterChromeHost(hostName, shimPath, ["chrome-extension://*/*"]);
        #else
        // TODO: Replace with actual Chrome Web Store extension ID after publishing
        // Format: "chrome-extension://[32-character-extension-id]/"
        manager.RegisterChromeHost(hostName, shimPath, ["chrome-extension://*/*"]);
        #endif
    }
}