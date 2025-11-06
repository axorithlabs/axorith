using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Axorith.Shared.Platform.Windows;

/// <summary>
///     Provides Windows-specific API for manipulating windows.
/// </summary>
public static class WindowApi
{
    /// <summary>
    ///     Moves a window to the specified monitor.
    /// </summary>
    /// <param name="windowHandle">The handle of the window to move.</param>
    /// <param name="monitorIndex">The index of the monitor to move the window to.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="monitorIndex" /> is out of range.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the process has no main window.</exception>
    public static void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex)
    {
        if (monitorIndex < 0) throw new ArgumentOutOfRangeException(nameof(monitorIndex));

        var monitors = NativeApi.GetMonitors();
        if (monitorIndex >= monitors.Length) throw new ArgumentOutOfRangeException(nameof(monitorIndex));

        if (windowHandle == IntPtr.Zero) throw new InvalidOperationException("Process has no main window.");

        if (!NativeApi.GetWindowRect(windowHandle, out var currentRect))
            throw new InvalidOperationException($"Failed to get window rect. Error: {Marshal.GetLastWin32Error()}");

        var width = currentRect.Right - currentRect.Left;
        var height = currentRect.Bottom - currentRect.Top;

        var target = monitors[monitorIndex];

        if (!NativeApi.GetWindowPlacement(windowHandle, out var placement))
            throw new InvalidOperationException(
                $"Failed to get window placement. Error: {Marshal.GetLastWin32Error()}");

        placement.Length = Marshal.SizeOf(typeof(NativeApi.WINDOWPLACEMENT));
        placement.ShowCmd = NativeApi.SW_SHOWNORMAL;
        placement.NormalPosition = new NativeApi.RECT
        {
            Left = target.Left,
            Top = target.Top,
            Right = target.Left + width,
            Bottom = target.Top + height
        };

        if (!NativeApi.SetWindowPlacement(windowHandle, ref placement))
            throw new InvalidOperationException(
                $"Failed to set window placement. Error: {Marshal.GetLastWin32Error()}");
    }

    /// <summary>
    ///     Asynchronously waits for a process's main window to be initialized.
    /// </summary>
    /// <param name="process">The process to monitor.</param>
    /// <param name="timeoutMs">The timeout in milliseconds.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="process" /> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the process exits before creating a main window.</exception>
    /// <exception cref="TimeoutException">Thrown when the process window does not appear in time.</exception>
    public static async Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000)
    {
        if (process == null) throw new ArgumentNullException(nameof(process));

        const int delay = 100;
        var stopwatch = Stopwatch.StartNew();

        while (process.MainWindowHandle == IntPtr.Zero)
        {
            await Task.Delay(delay);

            process.Refresh();

            if (process.HasExited)
                throw new InvalidOperationException("Process exited before creating a main window.");

            if (stopwatch.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Process window did not appear in time.");
        }
    }
}