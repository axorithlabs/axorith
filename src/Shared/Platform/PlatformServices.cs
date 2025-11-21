using System.Runtime.InteropServices;
using Axorith.Sdk.Services;
using Axorith.Shared.Platform.Linux;
using Axorith.Shared.Platform.MacOS;
using Axorith.Shared.Platform.Windows;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform;

/// <summary>
///     Platform-specific service registration.
///     Automatically selects the correct implementation based on the current OS.
/// </summary>
public static class PlatformServices
{
    /// <summary>
    ///     Creates platform-specific SecureStorage implementation.
    ///     Factory method for direct instantiation without DI container.
    /// </summary>
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
}