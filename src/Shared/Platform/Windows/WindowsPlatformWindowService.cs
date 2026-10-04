using System.Diagnostics;
using System.Runtime.Versioning;

namespace Axorith.Shared.Platform.Windows;

[SupportedOSPlatform("windows")]
internal class WindowsPlatformWindowService : IPlatformWindowService
{
    public Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        return WindowApi.WaitForWindowInitAsync(process, timeoutMs, cancellationToken);
    }

    public void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex) => WindowApi.MoveWindowToMonitor(windowHandle, monitorIndex);

    public void SetWindowState(IntPtr windowHandle, WindowState state) => WindowApi.SetWindowState(windowHandle, state);

    public WindowState GetWindowState(IntPtr windowHandle) => WindowApi.GetWindowState(windowHandle);

    public void SetWindowSize(IntPtr windowHandle, int width, int height) => WindowApi.SetWindowSize(windowHandle, width, height);

    public void SetWindowPosition(IntPtr windowHandle, int x, int y) => WindowApi.SetWindowPosition(windowHandle, x, y);

    public (int X, int Y, int Width, int Height) GetWindowBounds(IntPtr windowHandle) => WindowApi.GetWindowBounds(windowHandle);

    public void FocusWindow(IntPtr windowHandle) => WindowApi.FocusWindow(windowHandle);

    public int GetMonitorCount() => WindowApi.GetMonitorCount();

    public (int X, int Y, int Width, int Height) GetMonitorBounds(int monitorIndex) => WindowApi.GetMonitorBounds(monitorIndex);

    public string GetMonitorName(int monitorIndex) => WindowApi.GetMonitorName(monitorIndex);
}
