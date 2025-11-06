using Axorith.Shared.Utils;
using FluentAssertions;

namespace Axorith.Shared.Tests.Utils;

public class EnvironmentUtilsTests
{
    [Fact]
    public void GetCurrentPlatform_ShouldReturnValidPlatform()
    {
        // Act
        var platform = EnvironmentUtils.GetCurrentPlatform();

        // Assert
        platform.Should().BeOneOf(Sdk.Platform.Windows, Sdk.Platform.Linux, Sdk.Platform.MacOs);
    }

    [Fact]
    public void GetCurrentPlatform_OnWindows_ShouldReturnWindows()
    {
        // This test will only pass on Windows
        if (OperatingSystem.IsWindows())
        {
            // Act
            var platform = EnvironmentUtils.GetCurrentPlatform();

            // Assert
            platform.Should().Be(Sdk.Platform.Windows);
        }
    }

    [Fact]
    public void GetCurrentPlatform_OnLinux_ShouldReturnLinux()
    {
        // This test will only pass on Linux
        if (OperatingSystem.IsLinux())
        {
            // Act
            var platform = EnvironmentUtils.GetCurrentPlatform();

            // Assert
            platform.Should().Be(Sdk.Platform.Linux);
        }
    }

    [Fact]
    public void GetCurrentPlatform_OnMacOS_ShouldReturnMacOS()
    {
        // This test will only pass on macOS
        if (OperatingSystem.IsMacOS())
        {
            // Act
            var platform = EnvironmentUtils.GetCurrentPlatform();

            // Assert
            platform.Should().Be(Sdk.Platform.MacOs);
        }
    }

    [Fact]
    public void GetCurrentPlatform_CalledMultipleTimes_ShouldReturnSameValue()
    {
        // Act
        var platform1 = EnvironmentUtils.GetCurrentPlatform();
        var platform2 = EnvironmentUtils.GetCurrentPlatform();
        var platform3 = EnvironmentUtils.GetCurrentPlatform();

        // Assert
        platform1.Should().Be(platform2);
        platform2.Should().Be(platform3);
    }
}