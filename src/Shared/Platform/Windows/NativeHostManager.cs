using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Axorith.Shared.Platform.Windows;

/// <summary>
///     Manages native messaging host registration for browser extensions (Windows only).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeHostManager
{
    public const string NATIVE_MESSAGING_HOST_NAME = "axorith-nm-pipe";

    /// <summary>
    ///     Ensures the native messaging host is registered in the Windows registry for Firefox.
    /// </summary>
    public static void EnsureFirefoxHostRegistered(string pipeName, string manifestPath)
    {
        var keyPath = $@"Software\Mozilla\NativeMessagingHosts\{pipeName}";

        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        if (key == null)
            throw new InvalidOperationException($"Failed to create registry key: {keyPath}");

        key.SetValue("", manifestPath, RegistryValueKind.String);
    }
}