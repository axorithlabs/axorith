using Microsoft.Win32;
using System;
using System.IO;
using System.Runtime.Versioning;

namespace Axorith.Shared.Platform.Windows;

/// <summary>
/// Manages the registration of the native messaging host for browsers on Windows.
/// </summary>
public static class NativeHostManager
{
    private const string FirefoxRegistryKey = @"Software\Mozilla\NativeMessagingHosts\";
    // private const string ChromeRegistryKey = @"Software\Google\Chrome\NativeMessagingHosts\";

    /// <summary>
    /// Ensures that the native messaging host is correctly registered for Firefox.
    /// This method is safe to call on every application startup.
    /// </summary>
    /// <param name="hostName">The name of the host (e.g., "axorith"). Must match the name in the extension.</param>
    /// <param name="manifestPath">The full, absolute path to the host's manifest .json file.</param>
    /// <exception cref="InvalidOperationException">Thrown if registration fails.</exception>
    [SupportedOSPlatform("windows")]
    public static void EnsureFirefoxHostRegistered(string hostName, string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Native messaging host manifest file not found.", manifestPath);
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(FirefoxRegistryKey + hostName);

            if (key == null)
            {
                throw new InvalidOperationException($"Failed to create or open registry key for host '{hostName}'.");
            }

            var currentValue = key.GetValue(null) as string;
            if (currentValue != manifestPath)
            {
                key.SetValue(null, manifestPath);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to register native messaging host for Firefox. Please check permissions. Inner exception: {ex.Message}", ex);
        }
    }
}