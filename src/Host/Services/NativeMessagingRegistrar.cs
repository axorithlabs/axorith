using Axorith.Shared.Platform;
using Axorith.Telemetry;

namespace Axorith.Host.Services;

/// <summary>
///     A hosted service that runs once at startup to ensure the Native Messaging Host
///     is correctly registered with the browser.
/// </summary>
public class NativeMessagingRegistrar(
    INativeMessagingManager manager,
    ILogger<NativeMessagingRegistrar> logger,
    IConfiguration configuration) : IHostedService
{
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

        manager.RegisterFirefoxHost(hostName, shimPath, [Axorith.Shared.Utils.SiteBlockerExtensionIds.Firefox]);

        var allowedOriginsByBrowser = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["chrome"] = ReadExtensionIds("ChromeExtensionIds"),
            ["edge"] = ReadExtensionIds("EdgeExtensionIds"),
            ["chromium"] = ReadExtensionIds("ChromiumExtensionIds")
        };
        var configuredCount = allowedOriginsByBrowser.Values.Sum(origins => origins.Length);
        manager.RegisterChromeHost(hostName, shimPath, allowedOriginsByBrowser);
        if (configuredCount == 0)
        {
            logger.LogWarning(
                "No Chromium extension IDs are configured. Chrome, Edge, and Chromium native messaging is disabled. " +
                "Set SiteBlocker:NativeMessaging:<Browser>ExtensionIds to the installed extension IDs.");
        }
        else
        {
            logger.LogInformation("Registered Chromium Native Messaging Host for {Count} extension IDs.",
                configuredCount);
        }
    }

    private string[] ReadExtensionIds(string settingName) =>
        (configuration[$"SiteBlocker:NativeMessaging:{settingName}"] ?? string.Empty)
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsValidChromiumExtensionId)
            .Select(id => $"chrome-extension://{id}/")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static bool IsValidChromiumExtensionId(string id) =>
        id.Length == 32 && id.All(character => character is >= 'a' and <= 'p');
}
