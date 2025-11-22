using FluentAssertions;
using Xunit;

namespace Axorith.Contracts.Tests.Messages;

/// <summary>
///     Tests for gRPC operation result messages
/// </summary>
public class OperationResultTests
{
    [Fact]
    public void OperationResult_Success_ShouldSetCorrectly()
    {
        // Arrange & Act
        var result = new OperationResult
        {
            Success = true,
            Message = "Operation completed successfully"
        };

        // Assert
        result.Success.Should().BeTrue();
        result.Message.Should().Be("Operation completed successfully");
    }

    [Fact]
    public void OperationResult_Failure_ShouldSetCorrectly()
    {
        // Arrange & Act
        var result = new OperationResult
        {
            Success = false,
            Message = "Operation failed"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Operation failed");
    }

    [Fact]
    public void OperationResult_DefaultValues_ShouldBeValid()
    {
        // Arrange & Act
        var result = new OperationResult();

        // Assert
        result.Success.Should().BeFalse(); // Default for bool
        result.Message.Should().BeEmpty(); // Default for string in protobuf
    }
}