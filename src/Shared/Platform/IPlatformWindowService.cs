using System.Diagnostics;

namespace Axorith.Shared.Platform;

/// <summary>
///     Cross-platform window management service.
///     Implementations handle platform-specific window operations.
/// </summary>
public interface IPlatformWindowService
{
    Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000, CancellationToken cancellationToken = default);
    void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex);
    void SetWindowState(IntPtr windowHandle, WindowState state);
    WindowState GetWindowState(IntPtr windowHandle);
    void SetWindowSize(IntPtr windowHandle, int width, int height);
    void SetWindowPosition(IntPtr windowHandle, int x, int y);
    (int X, int Y, int Width, int Height) GetWindowBounds(IntPtr windowHandle);
    void FocusWindow(IntPtr windowHandle);
    int GetMonitorCount();
    (int X, int Y, int Width, int Height) GetMonitorBounds(int monitorIndex);
    string GetMonitorName(int monitorIndex);
}