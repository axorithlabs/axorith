using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Axorith.Shared.Platform.MacOS;

[SupportedOSPlatform("macos")]
internal static class MacOsWindowApi
{
    private const string HiservicesFramework = "/System/Library/Frameworks/ApplicationServices.framework/Versions/A/Frameworks/HIServices.framework/Versions/A/HIServices";
    private const string CoreGraphicsFramework = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    public static async Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.Now;

        while (!HasWindow(process))
        {
            if ((DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
            {
                throw new TimeoutException($"Process window did not appear within {timeoutMs}ms");
            }

            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(100, cancellationToken);
        }
    }

    public static void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex)
    {
        if (!IsAccessibilityEnabled())
        {
            throw new InvalidOperationException("Accessibility permission is required to move windows");
        }

        var position = GetDisplayPosition(monitorIndex);
        SetWindowPosition(windowHandle, position.X, position.Y);
    }

    public static bool IsAccessibilityEnabled()
    {
        return AXIsProcessTrustedWithOptions(IntPtr.Zero);
    }

    public static bool CheckAccessibilityWithRetry(int maxAttempts = 5, int delayMs = 1000)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            if (AXIsProcessTrustedWithOptions(IntPtr.Zero))
            {
                return true;
            }

            if (i < maxAttempts - 1)
            {
                Thread.Sleep(delayMs);
            }
        }

        return false;
    }

    private static bool HasWindow(Process process)
    {
        try
        {
            var appElement = AXUIElementCreateApplication(process.Id);
            if (appElement == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var windowsAttribute = CFStringCreateWithCString(IntPtr.Zero, "AXWindows");
                if (windowsAttribute == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    var result = AXUIElementCopyAttributeValue(appElement, windowsAttribute, out var windowsValue);
                    if (result == 0 && windowsValue != IntPtr.Zero)
                    {
                        try
                        {
                            var arraySize = CFArrayGetCount(windowsValue);
                            return arraySize > 0;
                        }
                        finally
                        {
                            CFRelease(windowsValue);
                        }
                    }
                }
                finally
                {
                    CFRelease(windowsAttribute);
                }
            }
            finally
            {
                CFRelease(appElement);
            }
        }
        catch (Exception)
        {
            // Accessibility API call failed — process has no accessible window
        }

        return false;
    }

    public static void SetWindowPosition(IntPtr windowHandle, int x, int y)
    {
        if (!IsAccessibilityEnabled())
        {
            throw new InvalidOperationException("Accessibility permission is required to move windows");
        }

        var positionAttribute = CFStringCreateWithCString(IntPtr.Zero, "AXPosition");
        if (positionAttribute == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create position attribute string");
        }

        try
        {
            var position = CreateAxValueFromPoint(x, y);
            try
            {
                var result = AXUIElementSetAttributeValue(windowHandle, positionAttribute, position);
                if (result != 0)
                {
                    throw new InvalidOperationException($"Failed to set window position. Error code: {result}");
                }
            }
            finally
            {
                CFRelease(position);
            }
        }
        finally
        {
            CFRelease(positionAttribute);
        }
    }

    public static void SetWindowSize(IntPtr windowHandle, int width, int height)
    {
        if (!IsAccessibilityEnabled())
        {
            throw new InvalidOperationException("Accessibility permission is required to resize windows");
        }

        var sizeAttribute = CFStringCreateWithCString(IntPtr.Zero, "AXSize");
        if (sizeAttribute == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create size attribute string");
        }

        try
        {
            var size = CreateAxValueFromSize(width, height);
            try
            {
                var result = AXUIElementSetAttributeValue(windowHandle, sizeAttribute, size);
                if (result != 0)
                {
                    throw new InvalidOperationException($"Failed to set window size. Error code: {result}");
                }
            }
            finally
            {
                CFRelease(size);
            }
        }
        finally
        {
            CFRelease(sizeAttribute);
        }
    }

    public static (int X, int Y, int Width, int Height) GetWindowBounds(IntPtr windowHandle)
    {
        if (!IsAccessibilityEnabled())
        {
            throw new InvalidOperationException("Accessibility permission is required to get window bounds");
        }

        var position = GetWindowPosition(windowHandle);
        var size = GetWindowSize(windowHandle);

        return (position.X, position.Y, size.Width, size.Height);
    }

    private static (int X, int Y) GetWindowPosition(IntPtr windowHandle)
    {
        var positionAttribute = CFStringCreateWithCString(IntPtr.Zero, "AXPosition");
        if (positionAttribute == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create position attribute string");
        }

        try
        {
            var result = AXUIElementCopyAttributeValue(windowHandle, positionAttribute, out var positionValue);
            if (result != 0 || positionValue == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to get window position. Error code: {result}");
            }

            try
            {
                return GetPointFromAxValue(positionValue);
            }
            finally
            {
                CFRelease(positionValue);
            }
        }
        finally
        {
            CFRelease(positionAttribute);
        }
    }

    private static (int Width, int Height) GetWindowSize(IntPtr windowHandle)
    {
        var sizeAttribute = CFStringCreateWithCString(IntPtr.Zero, "AXSize");
        if (sizeAttribute == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create size attribute string");
        }

        try
        {
            var result = AXUIElementCopyAttributeValue(windowHandle, sizeAttribute, out var sizeValue);
            if (result != 0 || sizeValue == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to get window size. Error code: {result}");
            }

            try
            {
                return GetSizeFromAxValue(sizeValue);
            }
            finally
            {
                CFRelease(sizeValue);
            }
        }
        finally
        {
            CFRelease(sizeAttribute);
        }
    }

    public static WindowState GetWindowState(IntPtr windowHandle)
    {
        if (!IsAccessibilityEnabled())
        {
            throw new InvalidOperationException("Accessibility permission is required to get window state");
        }

        var minimizedAttribute = CFStringCreateWithCString(IntPtr.Zero, "AXMinimized");
        if (minimizedAttribute == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create minimized attribute string");
        }

        try
        {
            var result = AXUIElementCopyAttributeValue(windowHandle, minimizedAttribute, out var minimizedValue);
            if (result != 0 || minimizedValue == IntPtr.Zero)
            {
                return WindowState.Normal;
            }

            try
            {
                var isMinimized = Marshal.ReadInt32(minimizedValue) != 0;
                return isMinimized ? WindowState.Minimized : WindowState.Normal;
            }
            finally
            {
                CFRelease(minimizedValue);
            }
        }
        finally
        {
            CFRelease(minimizedAttribute);
        }
    }

    public static void SetWindowState(IntPtr windowHandle, WindowState state)
    {
        if (!IsAccessibilityEnabled())
        {
            throw new InvalidOperationException("Accessibility permission is required to set window state");
        }

        var minimizedAttribute = CFStringCreateWithCString(IntPtr.Zero, "AXMinimized");
        if (minimizedAttribute == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create minimized attribute string");
        }

        try
        {
            var minimizedValue = state == WindowState.Minimized ? 1 : 0;
            var cfNumber = CFNumberCreate(IntPtr.Zero, 0, ref minimizedValue);
            if (cfNumber == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create CFNumber");
            }

            try
            {
                var result = AXUIElementSetAttributeValue(windowHandle, minimizedAttribute, cfNumber);
                if (result != 0)
                {
                    throw new InvalidOperationException($"Failed to set window state. Error code: {result}");
                }
            }
            finally
            {
                CFRelease(cfNumber);
            }
        }
        finally
        {
            CFRelease(minimizedAttribute);
        }
    }

    public static void FocusWindow(IntPtr windowHandle)
    {
        if (!IsAccessibilityEnabled())
        {
            throw new InvalidOperationException("Accessibility permission is required to focus windows");
        }

        var raiseAction = CFStringCreateWithCString(IntPtr.Zero, "AXRaise");
        if (raiseAction == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create raise action string");
        }

        try
        {
            var result = AXUIElementPerformAction(windowHandle, raiseAction);
            if (result != 0)
            {
                throw new InvalidOperationException($"Failed to raise window. Error code: {result}");
            }
        }
        finally
        {
            CFRelease(raiseAction);
        }
    }

    public static int GetMonitorCount()
    {
        var displays = new IntPtr[16];
        var count = CGGetActiveDisplayList(16, displays, out var displayCount);
        if (count != 0)
        {
            return 1;
        }

        return (int)displayCount;
    }

    public static (int X, int Y, int Width, int Height) GetMonitorBounds(int monitorIndex)
    {
        var displays = new IntPtr[16];
        var count = CGGetActiveDisplayList(16, displays, out var displayCount);
        if (count != 0 || monitorIndex >= displayCount)
        {
            return (0, 0, 1920, 1080);
        }

        var display = displays[monitorIndex];
        var bounds = CGDisplayBounds(display);

        return (bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    public static string GetMonitorName(int monitorIndex)
    {
        var displays = new IntPtr[16];
        var count = CGGetActiveDisplayList(16, displays, out var displayCount);
        if (count != 0 || monitorIndex >= displayCount)
        {
            return $"Display {monitorIndex + 1}";
        }

        return $"Display {monitorIndex + 1}";
    }

    private static (int X, int Y) GetDisplayPosition(int monitorIndex)
    {
        var bounds = GetMonitorBounds(monitorIndex);
        return (bounds.X + 50, bounds.Y + 50);
    }

    private static IntPtr CreateAxValueFromPoint(int x, int y)
    {
        var point = new CgPoint { X = x, Y = y };
        return CFTypeCreateWithPoint(point);
    }

    private static IntPtr CreateAxValueFromSize(int width, int height)
    {
        var size = new CgSize { Width = width, Height = height };
        return CFTypeCreateWithSize(size);
    }

    private static (int X, int Y) GetPointFromAxValue(IntPtr axValue)
    {
        var point = new CgPoint();
        CFTypeGetValueAsPoint(axValue, ref point);
        return ((int)point.X, (int)point.Y);
    }

    private static (int Width, int Height) GetSizeFromAxValue(IntPtr axValue)
    {
        var size = new CgSize();
        CFTypeGetValueAsSize(axValue, ref size);
        return ((int)size.Width, (int)size.Height);
    }

    [DllImport(HiservicesFramework)]
    private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    [DllImport(HiservicesFramework)]
    private static extern IntPtr AXUIElementCreateApplication(int pid);

    [DllImport(HiservicesFramework)]
    private static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);

    [DllImport(HiservicesFramework)]
    private static extern int AXUIElementSetAttributeValue(IntPtr element, IntPtr attribute, IntPtr value);

    [DllImport(HiservicesFramework)]
    private static extern int AXUIElementPerformAction(IntPtr element, IntPtr action);

    [DllImport(CoreGraphicsFramework)]
    private static extern int CGGetActiveDisplayList(uint maxDisplays, IntPtr[] displays, out uint displayCount);

    [DllImport(CoreGraphicsFramework)]
    private static extern CgRect CGDisplayBounds(IntPtr display);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string str);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFNumberCreate(IntPtr allocator, int type, ref int value);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFArrayGetCount(IntPtr array);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFTypeCreateWithPoint(CgPoint point);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFTypeCreateWithSize(CgSize size);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFTypeGetValueAsPoint(IntPtr type, ref CgPoint point);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFTypeGetValueAsSize(IntPtr type, ref CgSize size);

    [StructLayout(LayoutKind.Sequential)]
    private struct CgPoint
    {
        public float X;
        public float Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CgSize
    {
        public float Width;
        public float Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CgRect
    {
        public CgPoint Origin;
        public CgSize Size;

        public int X => (int)Origin.X;
        public int Y => (int)Origin.Y;
        public int Width => (int)Size.Width;
        public int Height => (int)Size.Height;
    }
}
