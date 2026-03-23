using Axorith.Client.ViewModels;
using FluentAssertions;
using Xunit;

namespace Axorith.Client.Tests.Versioning;

/// <summary>
///     Tests for ErrorViewModel - version conflict error display.
/// </summary>
public class ErrorViewModelTests
{
    [Fact]
    public void Configure_SetsErrorMessage()
    {
        // Arrange
        var vm = new ErrorViewModel();

        // Act
        vm.Configure("Test error", () => Task.CompletedTask);

        // Assert
        vm.ErrorMessage.Should().Be("Test error");
        vm.IsVersionConflict.Should().BeFalse();
        vm.RetryCommand.Should().NotBeNull();
        vm.UpdateAndRestartCommand.Should().BeNull();
    }

    [Fact]
    public void ConfigureVersionConflict_SetsVersionProperties()
    {
        // Arrange
        var vm = new ErrorViewModel();

        // Act
        vm.ConfigureVersionConflict("1.0.0", "2.0.0");

        // Assert
        vm.ClientVersion.Should().Be("1.0.0");
        vm.HostVersion.Should().Be("2.0.0");
        vm.IsVersionConflict.Should().BeTrue();
        vm.ErrorMessage.Should().Contain("1.0.0");
        vm.ErrorMessage.Should().Contain("2.0.0");
    }

    [Fact]
    public void ConfigureVersionConflict_WithUpdateCallback_SetsUpdateCommand()
    {
        // Arrange
        var vm = new ErrorViewModel();

        // Act
        vm.ConfigureVersionConflict("1.0.0", "2.0.0", updateCallback: () => Task.CompletedTask);

        // Assert
        vm.UpdateAndRestartCommand.Should().NotBeNull();
    }

    [Fact]
    public void ConfigureVersionConflict_WithRetryCallback_SetsRetryCommand()
    {
        // Arrange
        var vm = new ErrorViewModel();

        // Act
        vm.ConfigureVersionConflict("1.0.0", "2.0.0", retryCallback: () => Task.CompletedTask);

        // Assert
        vm.RetryCommand.Should().NotBeNull();
    }

    [Fact]
    public void ConfigureVersionConflict_WithoutCallbacks_HasNoCommands()
    {
        // Arrange
        var vm = new ErrorViewModel();

        // Act
        vm.ConfigureVersionConflict("1.0.0", "2.0.0");

        // Assert
        vm.UpdateAndRestartCommand.Should().BeNull();
        vm.RetryCommand.Should().BeNull();
    }

    [Fact]
    public void IsVersionConflict_WhenVersionsNotSet_ReturnsFalse()
    {
        // Arrange
        var vm = new ErrorViewModel();

        // Assert
        vm.IsVersionConflict.Should().BeFalse();
    }

    [Fact]
    public void IsVersionConflict_WhenOnlyClientVersionSet_ReturnsFalse()
    {
        // Arrange
        var vm = new ErrorViewModel { ClientVersion = "1.0.0" };

        // Assert
        vm.IsVersionConflict.Should().BeFalse();
    }

    [Fact]
    public void ErrorMessage_ContainsUpdateInstructions()
    {
        // Arrange
        var vm = new ErrorViewModel();

        // Act
        vm.ConfigureVersionConflict("1.2.0", "2.0.0");

        // Assert
        vm.ErrorMessage.Should().Contain("incompatible");
        vm.ErrorMessage.Should().Contain("update");
    }
}
