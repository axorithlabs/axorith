using System.Collections.Concurrent;
using FluentAssertions;

namespace Axorith.Sdk.Tests;

/// <summary>
///     Tests for ValidationResult factory methods and behavior
/// </summary>
public class ValidationResultTests
{
    [Fact]
    public void Success_ShouldReturnSameInstance()
    {
        // Act
        var result1 = ValidationResult.Success;
        var result2 = ValidationResult.Success;

        // Assert
        result1.Should().BeSameAs(result2, "Success should be a singleton");
    }

    [Fact]
    public void Success_ShouldHaveOkStatus()
    {
        // Act
        var result = ValidationResult.Success;

        // Assert
        result.Status.Should().Be(ValidationStatus.Ok);
        result.Message.Should().Be("Configuration is valid.");
    }

    [Fact]
    public void Fail_ShouldCreateErrorResult()
    {
        // Arrange
        var errorMessage = "Missing required field";

        // Act
        var result = ValidationResult.Fail(errorMessage);

        // Assert
        result.Status.Should().Be(ValidationStatus.Error);
        result.Message.Should().Be(errorMessage);
    }

    [Fact]
    public void Fail_WithEmptyMessage_ShouldAccept()
    {
        // Act
        var result = ValidationResult.Fail(string.Empty);

        // Assert
        result.Status.Should().Be(ValidationStatus.Error);
        result.Message.Should().BeEmpty();
    }

    [Fact]
    public void Fail_WithNullMessage_ShouldAccept()
    {
        // Act
        var result = ValidationResult.Fail(null!);

        // Assert
        result.Status.Should().Be(ValidationStatus.Error);
        result.Message.Should().BeNull();
    }

    [Fact]
    public void Warn_ShouldCreateWarningResult()
    {
        // Arrange
        var warningMessage = "API key is about to expire";

        // Act
        var result = ValidationResult.Warn(warningMessage);

        // Assert
        result.Status.Should().Be(ValidationStatus.Warning);
        result.Message.Should().Be(warningMessage);
    }

    [Fact]
    public void Warn_WithEmptyMessage_ShouldAccept()
    {
        // Act
        var result = ValidationResult.Warn(string.Empty);

        // Assert
        result.Status.Should().Be(ValidationStatus.Warning);
        result.Message.Should().BeEmpty();
    }

    [Fact]
    public void Warn_WithNullMessage_ShouldAccept()
    {
        // Act
        var result = ValidationResult.Warn(null!);

        // Assert
        result.Status.Should().Be(ValidationStatus.Warning);
        result.Message.Should().BeNull();
    }

    [Fact]
    public void Fail_CalledMultipleTimes_ShouldReturnDifferentInstances()
    {
        // Act
        var result1 = ValidationResult.Fail("Error 1");
        var result2 = ValidationResult.Fail("Error 2");

        // Assert
        result1.Should().NotBeSameAs(result2);
        result1.Message.Should().NotBe(result2.Message);
    }

    [Fact]
    public void Warn_CalledMultipleTimes_ShouldReturnDifferentInstances()
    {
        // Act
        var result1 = ValidationResult.Warn("Warning 1");
        var result2 = ValidationResult.Warn("Warning 2");

        // Assert
        result1.Should().NotBeSameAs(result2);
        result1.Message.Should().NotBe(result2.Message);
    }

    [Theory]
    [InlineData("Invalid email format")]
    [InlineData("API token is required")]
    [InlineData("Port must be between 1 and 65535")]
    [InlineData("Configuration contains errors:\n- Missing field A\n- Invalid field B")]
    public void Fail_WithVariousMessages_ShouldPreserveMessage(string message)
    {
        // Act
        var result = ValidationResult.Fail(message);

        // Assert
        result.Message.Should().Be(message);
        result.Status.Should().Be(ValidationStatus.Error);
    }

    [Theory]
    [InlineData("Deprecated setting will be removed in v2.0")]
    [InlineData("Consider upgrading to premium API")]
    [InlineData("Using default value for unset parameter")]
    public void Warn_WithVariousMessages_ShouldPreserveMessage(string message)
    {
        // Act
        var result = ValidationResult.Warn(message);

        // Assert
        result.Message.Should().Be(message);
        result.Status.Should().Be(ValidationStatus.Warning);
    }

    [Fact]
    public void Status_Property_ShouldBeReadOnly()
    {
        // Arrange
        var result = ValidationResult.Fail("Error");

        // Act & Assert - Status should not have a setter
        result.Status.Should().Be(ValidationStatus.Error);
        typeof(ValidationResult).GetProperty(nameof(ValidationResult.Status))!
            .CanWrite.Should().BeFalse("Status should be read-only");
    }

    [Fact]
    public void Message_Property_ShouldBeReadOnly()
    {
        // Arrange
        var result = ValidationResult.Fail("Error");

        // Act & Assert - Message should not have a setter
        result.Message.Should().Be("Error");
        typeof(ValidationResult).GetProperty(nameof(ValidationResult.Message))!
            .CanWrite.Should().BeFalse("Message should be read-only");
    }

    [Fact]
    public void Fail_WithLongMessage_ShouldHandleCorrectly()
    {
        // Arrange
        var longMessage = new string('x', 10000);

        // Act
        var result = ValidationResult.Fail(longMessage);

        // Assert
        result.Message.Should().HaveLength(10000);
        result.Status.Should().Be(ValidationStatus.Error);
    }

    [Fact]
    public void Warn_WithSpecialCharacters_ShouldPreserveExactly()
    {
        // Arrange
        var specialMessage = "Error: <xml>\n\t\"quotes\" & symbols: @#$%^&*()";

        // Act
        var result = ValidationResult.Warn(specialMessage);

        // Assert
        result.Message.Should().Be(specialMessage);
    }

    [Fact]
    public void Success_CalledMultipleTimesInParallel_ShouldReturnSameInstance()
    {
        // Arrange
        var results = new ConcurrentBag<ValidationResult>();

        // Act - access from multiple threads
        Parallel.For(0, 100, _ => { results.Add(ValidationResult.Success); });

        // Assert - all should be the same instance
        results.Should().AllSatisfy(r => r.Should().BeSameAs(ValidationResult.Success));
    }

    [Fact]
    public void Fail_CalledInParallel_ShouldNotThrow()
    {
        // Arrange
        var results = new ConcurrentBag<ValidationResult>();

        // Act
        var act = () => Parallel.For(0, 100, i => { results.Add(ValidationResult.Fail($"Error {i}")); });

        // Assert
        act.Should().NotThrow();
        results.Should().HaveCount(100);
    }

    [Fact]
    public void ValidationResults_WithDifferentStatusTypes_ShouldBeDistinguishable()
    {
        // Arrange
        var success = ValidationResult.Success;
        var warning = ValidationResult.Warn("Warning");
        var error = ValidationResult.Fail("Error");

        // Assert
        success.Status.Should().NotBe(warning.Status);
        success.Status.Should().NotBe(error.Status);
        warning.Status.Should().NotBe(error.Status);
    }
}