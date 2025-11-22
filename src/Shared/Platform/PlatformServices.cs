using System.Runtime.InteropServices;
using Axorith.Sdk.Services;
using Axorith.Shared.Platform.Linux;
using Axorith.Shared.Platform.MacOS;
using Axorith.Shared.Platform.Windows;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform;

public static class PlatformServices
{
    public static ISecureStorageService CreateSecureStorage(ILogger logger)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsSecureStorage(logger);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxSecureStorage(logger);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOsSecureStorage(logger);
        }

        throw new PlatformNotSupportedException(
            $"Secure storage is not supported on this platform: {RuntimeInformation.OSDescription}");
    }

    public static IAppDiscoveryService CreateAppDiscoveryService(ILoggerFactory loggerFactory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsAppDiscoveryService(loggerFactory.CreateLogger<WindowsAppDiscoveryService>());
        }

        throw new PlatformNotSupportedException(
            $"App discovery is not supported on this platform: {RuntimeInformation.OSDescription}");
    }

    public static IProcessBlocker CreateProcessBlocker(ILogger logger)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsProcessBlocker(logger);
        }
        
        throw new PlatformNotSupportedException(
            $"Process blocker is not supported on this platform: {RuntimeInformation.OSDescription}");
    }

    public static ISystemNotificationService CreateNotificationService(ILogger logger)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsSystemNotificationService(logger);
        }

        // Fallback for other platforms (noop for now to avoid crashes)
        return new NoOpNotificationService();
    }
}

// Simple fallback to avoid breaking Linux/Mac builds until implemented
file class NoOpNotificationService : ISystemNotificationService
{
    public Task ShowNotificationAsync(string title, string message, TimeSpan? expiration = null)
    {
        return Task.CompletedTask;
    }
}