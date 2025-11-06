using Axorith.Shared.Platform.Windows;
using FluentAssertions;

namespace Axorith.Shared.Tests.Platform.Windows;

public class WindowApiTests
{
    [Fact]
    public void MoveWindowToMonitor_InvalidMonitorIndex_ShouldThrow()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows-only

        // Arrange
        var invalidIndex = -1;

        // Act
        var act = () => WindowApi.MoveWindowToMonitor(IntPtr.Zero, invalidIndex);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MoveWindowToMonitor_ZeroHandle_ShouldThrow()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows-only

        // Arrange
        var monitorIndex = 0;

        // Act
        var act = () => WindowApi.MoveWindowToMonitor(IntPtr.Zero, monitorIndex);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task WaitForWindowInitAsync_NullProcess_ShouldThrow()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows-only

        // Act
        var act = async () => await WindowApi.WaitForWindowInitAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}