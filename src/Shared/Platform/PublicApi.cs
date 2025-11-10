using System.Diagnostics;

namespace Axorith.Shared.Platform;

/// <summary>
///     Public API for platform-specific functionality.
///     Provides cross-platform abstractions for window management, native hosting, etc.
/// </summary>
public static class PublicApi
{
    /// <summary>
    ///     Waits for a process to create its main window (Windows only).
    /// </summary>
    public static async Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Window management is only supported on Windows");

        await Windows.WindowApi.WaitForWindowInitAsync(process, timeoutMs, cancellationToken);
    }

    /// <summary>
    ///     Moves a window to a specific monitor (Windows only).
    /// </summary>
    public static void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Window management is only supported on Windows");

        Windows.WindowApi.MoveWindowToMonitor(windowHandle, monitorIndex);
    }

    /// <summary>
    ///     Gets the native messaging host name for the browser extension (Windows only).
    /// </summary>
    public static string GetNativeMessagingHostName()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Native messaging host is only supported on Windows");

        return Windows.NativeHostManager.NativeMessagingHostName;
    }

    /// <summary>
    ///     Ensures Firefox native messaging host is registered (Windows only).
    /// </summary>
    public static void EnsureFirefoxHostRegistered(string pipeName, string manifestPath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Native messaging host registration is only supported on Windows");

        Windows.NativeHostManager.EnsureFirefoxHostRegistered(pipeName, manifestPath);
    }
}
