using System.Diagnostics;
using System.Runtime.Versioning;

namespace Axorith.Shared.Platform.MacOS;

[SupportedOSPlatform("macos")]
internal class MacOsPlatformWindowService : IPlatformWindowService
{
    public Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        return MacOsWindowApi.WaitForWindowInitAsync(process, timeoutMs, cancellationToken);
    }

    public void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex)
    {
        MacOsWindowApi.MoveWindowToMonitor(windowHandle, monitorIndex);
    }

    public void SetWindowState(IntPtr windowHandle, WindowState state)
    {
        MacOsWindowApi.SetWindowState(windowHandle, state);
    }

    public WindowState GetWindowState(IntPtr windowHandle)
    {
        return MacOsWindowApi.GetWindowState(windowHandle);
    }

    public void SetWindowSize(IntPtr windowHandle, int width, int height)
    {
        MacOsWindowApi.SetWindowSize(windowHandle, width, height);
    }

    public void SetWindowPosition(IntPtr windowHandle, int x, int y)
    {
        MacOsWindowApi.SetWindowPosition(windowHandle, x, y);
    }

    public (int X, int Y, int Width, int Height) GetWindowBounds(IntPtr windowHandle)
    {
        return MacOsWindowApi.GetWindowBounds(windowHandle);
    }

    public void FocusWindow(IntPtr windowHandle)
    {
        MacOsWindowApi.FocusWindow(windowHandle);
    }

    public int GetMonitorCount()
    {
        return MacOsWindowApi.GetMonitorCount();
    }

    public (int X, int Y, int Width, int Height) GetMonitorBounds(int monitorIndex)
    {
        return MacOsWindowApi.GetMonitorBounds(monitorIndex);
    }

    public string GetMonitorName(int monitorIndex)
    {
        return MacOsWindowApi.GetMonitorName(monitorIndex);
    }
}
