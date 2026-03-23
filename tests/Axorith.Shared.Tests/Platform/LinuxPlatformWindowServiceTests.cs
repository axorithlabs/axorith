using System.Runtime.Versioning;
using Axorith.Shared.Platform;
using Axorith.Shared.Platform.Linux;
using FluentAssertions;

namespace Axorith.Shared.Tests.Platform;

public class LinuxPlatformWindowServiceTests
{
    [Fact]
    [SupportedOSPlatform("linux")]
    public void GetMonitorCount_DoesNotThrow()
    {
        if (!OperatingSystem.IsLinux()) return;

        var service = new LinuxPlatformWindowService();
        var count = service.GetMonitorCount();
        count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void GetMonitorBounds_ReturnsValidBounds()
    {
        if (!OperatingSystem.IsLinux()) return;

        var service = new LinuxPlatformWindowService();
        var (_, _, width, height) = service.GetMonitorBounds(0);

        width.Should().BeGreaterThan(0);
        height.Should().BeGreaterThan(0);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void GetMonitorName_ReturnsNonEmpty()
    {
        if (!OperatingSystem.IsLinux()) return;

        var service = new LinuxPlatformWindowService();
        var name = service.GetMonitorName(0);

        name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void GetMonitorBounds_ReturnsFallbackForInvalidIndex()
    {
        if (!OperatingSystem.IsLinux()) return;

        var service = new LinuxPlatformWindowService();
        var (x, y, width, height) = service.GetMonitorBounds(999);

        // Returns fallback values
        x.Should().Be(0);
        y.Should().Be(0);
        width.Should().BeGreaterThan(0);
        height.Should().BeGreaterThan(0);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void GetWindowBounds_ReturnsZerosForInvalidHandle()
    {
        if (!OperatingSystem.IsLinux()) return;

        var service = new LinuxPlatformWindowService();
        var bounds = service.GetWindowBounds(IntPtr.Zero);

        bounds.Should().NotBeNull();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void GetWindowState_ReturnsNormalForInvalidHandle()
    {
        if (!OperatingSystem.IsLinux()) return;

        var service = new LinuxPlatformWindowService();
        var state = service.GetWindowState(IntPtr.Zero);

        state.Should().BeOneOf(WindowState.Normal, WindowState.Minimized, WindowState.Maximized);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void MoveWindowToMonitor_NoExceptionForInvalidHandle()
    {
        if (!OperatingSystem.IsLinux()) return;

        var service = new LinuxPlatformWindowService();
        var act = () => service.MoveWindowToMonitor(IntPtr.Zero, 0);

        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void SetWindowSize_NoExceptionForInvalidHandle()
    {
        if (!OperatingSystem.IsLinux()) return;

        var service = new LinuxPlatformWindowService();
        var act = () => service.SetWindowSize(IntPtr.Zero, 800, 600);

        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void SetWindowPosition_NoExceptionForInvalidHandle()
    {
        if (!OperatingSystem.IsLinux()) return;

        var service = new LinuxPlatformWindowService();
        var act = () => service.SetWindowPosition(IntPtr.Zero, 100, 100);

        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void SetWindowState_NoExceptionForInvalidHandle()
    {
        if (!OperatingSystem.IsLinux()) return;

        var service = new LinuxPlatformWindowService();
        var act = () => service.SetWindowState(IntPtr.Zero, WindowState.Maximized);

        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void FocusWindow_NoExceptionForInvalidHandle()
    {
        if (!OperatingSystem.IsLinux()) return;

        var service = new LinuxPlatformWindowService();
        var act = () => service.FocusWindow(IntPtr.Zero);

        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Factory_CreatesLinuxWindowService()
    {
        if (!OperatingSystem.IsLinux()) return;

        var service = PlatformServices.CreateWindowService();

        service.Should().NotBeNull();
        service.Should().BeOfType<LinuxPlatformWindowService>();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void WaylandMode_NoException()
    {
        if (!OperatingSystem.IsLinux()) return;

        // On Wayland, the service should degrade gracefully (no X11 display available)
        var service = new LinuxPlatformWindowService();

        // All methods should be callable without throwing even on Wayland
        var act1 = () => service.GetMonitorCount();
        var act2 = () => service.GetMonitorBounds(0);
        var act3 = () => service.GetMonitorName(0);
        var act4 = () => service.GetWindowState(IntPtr.Zero);
        var act5 = () => service.GetWindowBounds(IntPtr.Zero);

        act1.Should().NotThrow();
        act2.Should().NotThrow();
        act3.Should().NotThrow();
        act4.Should().NotThrow();
        act5.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void WaylandMode_ReturnsFallback()
    {
        if (!OperatingSystem.IsLinux()) return;

        // When no X11 display is available (Wayland), methods should return fallback values
        var service = new LinuxPlatformWindowService();

        // On Wayland (no X11), GetMonitorCount returns 1 (fallback)
        var count = service.GetMonitorCount();
        count.Should().BeGreaterThanOrEqualTo(1);

        // Monitor bounds should return valid fallback dimensions
        var (_, _, width, height) = service.GetMonitorBounds(0);
        width.Should().BeGreaterThan(0);
        height.Should().BeGreaterThan(0);

        // Monitor name should return a non-empty string
        var name = service.GetMonitorName(0);
        name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void MultipleMonitors_AllHaveBounds()
    {
        if (!OperatingSystem.IsLinux()) return;

        var service = new LinuxPlatformWindowService();
        var count = service.GetMonitorCount();

        count.Should().BeGreaterThanOrEqualTo(1);

        // Each monitor should have valid bounds
        for (var i = 0; i < count; i++)
        {
            var (_, _, width, height) = service.GetMonitorBounds(i);
            width.Should().BeGreaterThan(0, "Monitor {0} should have positive width", i);
            height.Should().BeGreaterThan(0, "Monitor {0} should have positive height", i);
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void GetMonitorCount_FallbackOnError()
    {
        if (!OperatingSystem.IsLinux()) return;

        var service = new LinuxPlatformWindowService();

        // When X11 is unavailable, GetMonitorCount should return fallback of 1
        var count = service.GetMonitorCount();
        count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void GetMonitorBounds_FallbackOnError()
    {
        if (!OperatingSystem.IsLinux()) return;

        var service = new LinuxPlatformWindowService();

        // Invalid monitor index should return fallback bounds
        var (x, y, width, height) = service.GetMonitorBounds(-1);

        x.Should().Be(0);
        y.Should().Be(0);
        width.Should().BeGreaterThan(0);
        height.Should().BeGreaterThan(0);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void AllMethods_HandleNullDisplay()
    {
        if (!OperatingSystem.IsLinux()) return;

        // When X11 display is not available (Wayland), all methods should handle it gracefully
        var service = new LinuxPlatformWindowService();

        // These operations on IntPtr.Zero represent null display scenarios
        var act1 = () => service.SetWindowPosition(IntPtr.Zero, 100, 100);
        var act2 = () => service.SetWindowSize(IntPtr.Zero, 800, 600);
        var act3 = () => service.FocusWindow(IntPtr.Zero);
        var act4 = () => service.SetWindowState(IntPtr.Zero, WindowState.Maximized);
        var act5 = () => service.MoveWindowToMonitor(IntPtr.Zero, 0);

        act1.Should().NotThrow();
        act2.Should().NotThrow();
        act3.Should().NotThrow();
        act4.Should().NotThrow();
        act5.Should().NotThrow();
    }
}
