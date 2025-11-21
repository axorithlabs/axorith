using System.Runtime.InteropServices;
using Axorith.Sdk;

namespace Axorith.Shared.Utils;

/// <summary>
///     Provides utility methods related to the execution environment.
/// </summary>
public static class EnvironmentUtils
{
    /// <summary>
    ///     Determines the current operating system and returns the corresponding Axorith Platform enum.
    /// </summary>
    /// <returns>The current <see cref="Platform" />.</returns>
    /// <exception cref="NotSupportedException">Thrown if the current OS is not supported.</exception>
    public static Platform GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Platform.Windows;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Platform.Linux;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Platform.MacOs;
        }

        throw new NotSupportedException("Current operating system is not supported by Axorith.");
    }
}