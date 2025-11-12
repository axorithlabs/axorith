using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Axorith.Shared.Platform;

/// <summary>
///     Public API for platform-specific functionality.
///     Provides cross-platform abstractions for window management, native hosting, etc.
/// </summary>
public static class PublicApi
{
    /// <summary>
    ///     Waits for a process to create its main window (cross-platform).
    /// </summary>
    public static async Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            await Windows.WindowApi.WaitForWindowInitAsync(process, timeoutMs, cancellationToken);
        }
        else if (OperatingSystem.IsLinux())
        {
            await Linux.LinuxWindowApi.WaitForWindowInitAsync(process, timeoutMs, cancellationToken);
        }
        else if (OperatingSystem.IsMacOS())
        {
            await MacOS.MacOSWindowApi.WaitForWindowInitAsync(process, timeoutMs, cancellationToken);
        }
        else
        {
            throw new PlatformNotSupportedException($"Window management is not supported on this platform: {RuntimeInformation.OSDescription}");
        }
    }

    /// <summary>
    ///     Moves a window to a specific monitor (cross-platform).
    /// </summary>
    public static void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex)
    {
        if (OperatingSystem.IsWindows())
        {
            Windows.WindowApi.MoveWindowToMonitor(windowHandle, monitorIndex);
        }
        else if (OperatingSystem.IsLinux())
        {
            Linux.LinuxWindowApi.MoveWindowToMonitor(windowHandle, monitorIndex);
        }
        else if (OperatingSystem.IsMacOS())
        {
            MacOS.MacOSWindowApi.MoveWindowToMonitor(windowHandle, monitorIndex);
        }
        else
        {
            throw new PlatformNotSupportedException($"Window management is not supported on this platform: {RuntimeInformation.OSDescription}");
        }
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

    /// <summary>
    ///     Finds running processes by name or path (Windows only for now).
    /// </summary>
    public static List<Process> FindProcesses(string processNameOrPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return Windows.WindowApi.FindProcesses(processNameOrPath);
        }

        // Fallback to simple name-based search
        var processName = Path.GetFileNameWithoutExtension(processNameOrPath);
        return Process.GetProcessesByName(processName).ToList();
    }

    /// <summary>
    ///     Sets window state (Normal, Minimized, Maximized) - Windows only for now.
    /// </summary>
    public static void SetWindowState(IntPtr windowHandle, Windows.WindowState state)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("SetWindowState is currently only supported on Windows");

        Windows.WindowApi.SetWindowState(windowHandle, state);
    }

    /// <summary>
    ///     Gets current window state - Windows only for now.
    /// </summary>
    public static Windows.WindowState GetWindowState(IntPtr windowHandle)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("GetWindowState is currently only supported on Windows");

        return Windows.WindowApi.GetWindowState(windowHandle);
    }

    /// <summary>
    ///     Sets window size - Windows only for now.
    /// </summary>
    public static void SetWindowSize(IntPtr windowHandle, int width, int height)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("SetWindowSize is currently only supported on Windows");

        Windows.WindowApi.SetWindowSize(windowHandle, width, height);
    }

    /// <summary>
    ///     Sets window position - Windows only for now.
    /// </summary>
    public static void SetWindowPosition(IntPtr windowHandle, int x, int y)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("SetWindowPosition is currently only supported on Windows");

        Windows.WindowApi.SetWindowPosition(windowHandle, x, y);
    }

    /// <summary>
    ///     Gets window bounds - Windows only for now.
    /// </summary>
    public static (int X, int Y, int Width, int Height) GetWindowBounds(IntPtr windowHandle)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("GetWindowBounds is currently only supported on Windows");

        return Windows.WindowApi.GetWindowBounds(windowHandle);
    }

    /// <summary>
    ///     Brings window to foreground - Windows only for now.
    /// </summary>
    public static void FocusWindow(IntPtr windowHandle)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("FocusWindow is currently only supported on Windows");

        Windows.WindowApi.FocusWindow(windowHandle);
    }

    /// <summary>
    ///     Gets monitor count - Windows only for now.
    /// </summary>
    public static int GetMonitorCount()
    {
        if (OperatingSystem.IsWindows())
        {
            return Windows.WindowApi.GetMonitorCount();
        }

        return 1; // Fallback
    }
}
