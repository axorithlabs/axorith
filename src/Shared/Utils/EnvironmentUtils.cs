using System.Runtime.InteropServices;
using Axorith.Sdk;

namespace Axorith.Shared.Utils;

public static class EnvironmentUtils
{
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
