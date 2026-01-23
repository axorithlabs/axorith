using System.Diagnostics;
using System.Runtime.Versioning;

namespace Axorith.Shared.Platform.Linux;

[SupportedOSPlatform("linux")]
internal class LinuxPlatformWindowService : IPlatformWindowService
{
    public Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        return LinuxWindowApi.WaitForWindowInitAsync(process, timeoutMs, cancellationToken);
    }

    public void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex)
    {
        LinuxWindowApi.MoveWindowToMonitor(windowHandle, monitorIndex);
    }

    public void SetWindowState(IntPtr windowHandle, WindowState state)
    {
        throw new PlatformNotSupportedException("SetWindowState is not yet implemented on Linux");
    }

    public WindowState GetWindowState(IntPtr windowHandle)
    {
        throw new PlatformNotSupportedException("GetWindowState is not yet implemented on Linux");
    }

    public void SetWindowSize(IntPtr windowHandle, int width, int height)
    {
        throw new PlatformNotSupportedException("SetWindowSize is not yet implemented on Linux");
    }

    public void SetWindowPosition(IntPtr windowHandle, int x, int y)
    {
        throw new PlatformNotSupportedException("SetWindowPosition is not yet implemented on Linux");
    }

    public (int X, int Y, int Width, int Height) GetWindowBounds(IntPtr windowHandle)
    {
        throw new PlatformNotSupportedException("GetWindowBounds is not yet implemented on Linux");
    }

    public void FocusWindow(IntPtr windowHandle)
    {
        throw new PlatformNotSupportedException("FocusWindow is not yet implemented on Linux");
    }

    public int GetMonitorCount()
    {
        return 1;
    }

    public (int X, int Y, int Width, int Height) GetMonitorBounds(int monitorIndex)
    {
        throw new PlatformNotSupportedException("GetMonitorBounds is not yet implemented on Linux");
    }

    public string GetMonitorName(int monitorIndex)
    {
        return $"Monitor {monitorIndex + 1}";
    }
}