using System.Diagnostics;
using System.Runtime.Versioning;

namespace Axorith.Shared.Platform.Linux;

[SupportedOSPlatform("linux")]
internal sealed class LinuxPlatformWindowService : IPlatformWindowService
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
        LinuxWindowApi.SetWindowState(windowHandle, state);
    }

    public WindowState GetWindowState(IntPtr windowHandle)
    {
        return LinuxWindowApi.GetWindowState(windowHandle);
    }

    public void SetWindowSize(IntPtr windowHandle, int width, int height)
    {
        LinuxWindowApi.SetWindowSize(windowHandle, width, height);
    }

    public void SetWindowPosition(IntPtr windowHandle, int x, int y)
    {
        LinuxWindowApi.SetWindowPosition(windowHandle, x, y);
    }

    public (int X, int Y, int Width, int Height) GetWindowBounds(IntPtr windowHandle)
    {
        return LinuxWindowApi.GetWindowBounds(windowHandle);
    }

    public void FocusWindow(IntPtr windowHandle)
    {
        LinuxWindowApi.FocusWindow(windowHandle);
    }

    public int GetMonitorCount()
    {
        return LinuxWindowApi.GetMonitorCount();
    }

    public (int X, int Y, int Width, int Height) GetMonitorBounds(int monitorIndex)
    {
        return LinuxWindowApi.GetMonitorBounds(monitorIndex);
    }

    public string GetMonitorName(int monitorIndex)
    {
        return LinuxWindowApi.GetMonitorName(monitorIndex);
    }
}
