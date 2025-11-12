using Axorith.Sdk.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace Axorith.Shared.Platform;

/// <summary>
///     Platform-specific service registration.
///     Automatically selects the correct implementation based on the current OS.
/// </summary>
public static class PlatformServices
{
    /// <summary>
    ///     Registers platform-specific services in the DI container.
    ///     Automatically detects Windows, Linux, or macOS and registers appropriate implementations.
    /// </summary>
    public static IServiceCollection AddPlatformServices(this IServiceCollection services)
    {
        // Register SecureStorage with platform-specific implementation
        services.AddSingleton<ISecureStorageService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<ISecureStorageService>>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new Windows.WindowsSecureStorage(logger);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return new Linux.LinuxSecureStorage(logger);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return new MacOS.MacOSSecureStorage(logger);
            }

            throw new PlatformNotSupportedException(
                $"Secure storage is not supported on this platform: {RuntimeInformation.OSDescription}");
        });

        // Register other platform-specific services here as needed
        // Example: INativeWindowManager, IPlatformUtils, etc.

        return services;
    }

    /// <summary>
    ///     Creates platform-specific SecureStorage implementation.
    ///     Factory method for direct instantiation without DI container.
    /// </summary>
    public static ISecureStorageService CreateSecureStorage(ILogger logger)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new Windows.WindowsSecureStorage(logger);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new Linux.LinuxSecureStorage(logger);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOS.MacOSSecureStorage(logger);
        }

        throw new PlatformNotSupportedException(
            $"Secure storage is not supported on this platform: {RuntimeInformation.OSDescription}");
    }

    /// <summary>
    ///     Gets the current platform name for logging/diagnostics.
    /// </summary>
    public static string GetPlatformName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "Windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "Linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "macOS";
        
        return "Unknown";
    }
}
