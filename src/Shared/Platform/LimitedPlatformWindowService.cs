using System.Diagnostics;

namespace Axorith.Shared.Platform;

internal abstract class LimitedPlatformWindowService(string platformName) : IPlatformWindowService
{
    public abstract Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000,
        CancellationToken cancellationToken = default);

    public abstract void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex);

    public void SetWindowState(IntPtr windowHandle, WindowState state) => Unsupported();
    public WindowState GetWindowState(IntPtr windowHandle) => throw UnsupportedException();
    public void SetWindowSize(IntPtr windowHandle, int width, int height) => Unsupported();
    public void SetWindowPosition(IntPtr windowHandle, int x, int y) => Unsupported();
    public (int X, int Y, int Width, int Height) GetWindowBounds(IntPtr windowHandle) => throw UnsupportedException();
    public void FocusWindow(IntPtr windowHandle) => Unsupported();
    public virtual int GetMonitorCount() => 1;
    public (int X, int Y, int Width, int Height) GetMonitorBounds(int monitorIndex) => throw UnsupportedException();
    public virtual string GetMonitorName(int monitorIndex) => $"Monitor {monitorIndex + 1}";

    private PlatformNotSupportedException UnsupportedException() =>
        new($"Window operation is not yet implemented on {platformName}");

    private void Unsupported() => throw UnsupportedException();
}
