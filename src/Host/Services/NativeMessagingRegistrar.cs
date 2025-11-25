using Axorith.Shared.Platform;

namespace Axorith.Host.Services;

/// <summary>
///     A hosted service that runs once at startup to ensure the Native Messaging Host
///     is correctly registered with the browser.
/// </summary>
public class NativeMessagingRegistrar(
    INativeMessagingManager manager,
    IHostEnvironment environment,
    ILogger<NativeMessagingRegistrar> logger) : IHostedService
{
    private const string ExtensionId = "site-blocker-firefox@axorithlabs.com";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            RegisterHost();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to register Native Messaging Host. Site Blocker functionality may be unavailable.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void RegisterHost()
    {
        #if DEBUG
        var hostName = "axorith.dev";
        #else
        var hostName =  "axorith";
        #endif
        
        logger.LogInformation("Registering Native Messaging Host as '{HostName}' (Env: {Env})", 
            hostName, environment.EnvironmentName);

        var baseDir = AppContext.BaseDirectory;
        var shimPath = Path.GetFullPath(Path.Combine(baseDir, "..", "Axorith.Shim", "Axorith.Shim.exe"));

        if (!File.Exists(shimPath))
        {
            logger.LogWarning("Axorith.Shim.exe not found at expected path: {Path}. Skipping registration.", shimPath);
            return;
        }

        logger.LogInformation("Found Shim executable at: {Path}", shimPath);

        manager.RegisterFirefoxHost(hostName, shimPath, [ExtensionId]);
    }
}