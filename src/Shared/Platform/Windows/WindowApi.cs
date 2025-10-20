using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Axorith.Shared.Platform.Windows;

public static class WindowApi
{
    public static void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex)
    {
        if (monitorIndex < 0) throw new ArgumentOutOfRangeException(nameof(monitorIndex));

        var monitors = NativeApi.GetMonitors();
        if (monitorIndex >= monitors.Length) throw new ArgumentOutOfRangeException(nameof(monitorIndex));

        if (windowHandle == IntPtr.Zero) throw new InvalidOperationException("Process has no main window.");

        NativeApi.GetWindowRect(windowHandle, out NativeApi.RECT currentRect);
        var width = currentRect.Right - currentRect.Left;
        var height = currentRect.Bottom - currentRect.Top;

        NativeApi.RECT target = monitors[monitorIndex];

        NativeApi.GetWindowPlacement(windowHandle, out NativeApi.WINDOWPLACEMENT placement);
        placement.Length = Marshal.SizeOf(typeof(NativeApi.WINDOWPLACEMENT));
        placement.ShowCmd = NativeApi.SW_SHOWNORMAL;
        placement.NormalPosition = new NativeApi.RECT
        {
            Left = target.Left,
            Top = target.Top,
            Right = target.Left + width,
            Bottom = target.Top + height
        };
        NativeApi.SetWindowPlacement(windowHandle, ref placement);
    }
    
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