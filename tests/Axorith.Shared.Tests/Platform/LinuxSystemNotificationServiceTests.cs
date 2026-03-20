using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Axorith.Shared.Platform.Linux;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Shared.Tests.Platform;

public class LinuxSystemNotificationServiceTests
{
    [Fact]
    [SupportedOSPlatform("linux")]
    public async Task ShowNotificationAsync_DoesNotThrow_WhenDbusUnavailable()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxSystemNotificationService>.Instance;
        using var service = new LinuxSystemNotificationService(logger);

        // Should not throw even when D-Bus is unavailable
        var act = async () => await service.ShowNotificationAsync("Test Title", "Test Message");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public async Task ShowNotificationAsync_WithExpiration_DoesNotThrow()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxSystemNotificationService>.Instance;
        using var service = new LinuxSystemNotificationService(logger);

        var expiration = TimeSpan.FromSeconds(5);
        var act = async () => await service.ShowNotificationAsync("Test Title", "Test Body", expiration);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public async Task ShowNotificationAsync_DoesNotThrow_WhenCalledMultipleTimes()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxSystemNotificationService>.Instance;
        using var service = new LinuxSystemNotificationService(logger);

        for (var i = 0; i < 5; i++)
        {
            var act = async () =>
                await service.ShowNotificationAsync($"Title {i}", $"Message {i}");
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Constructor_DoesNotThrow()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxSystemNotificationService>.Instance;
        var act = () => new LinuxSystemNotificationService(logger);
        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Dispose_IsIdempotent()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxSystemNotificationService>.Instance;
        var service = new LinuxSystemNotificationService(logger);

        // First dispose
        service.Dispose();

        // Second dispose should be safe (no exception)
        var act = () => service.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public async Task ShowNotificationAsync_IsSafeAfterDispose()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxSystemNotificationService>.Instance;
        var service = new LinuxSystemNotificationService(logger);
        service.Dispose();

        // Should not throw after disposal
        var act = async () => await service.ShowNotificationAsync("Test", "Message");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public async Task ShowNotificationAsync_HandlesEmptyTitleAndMessage()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var logger = NullLogger<LinuxSystemNotificationService>.Instance;
        using var service = new LinuxSystemNotificationService(logger);

        var act = async () => await service.ShowNotificationAsync(string.Empty, string.Empty);
        await act.Should().NotThrowAsync();
    }
}
