using FluentAssertions;

namespace Axorith.Sdk.Tests;

/// <summary>
///     Tests for ValidationStatus enum
/// </summary>
public class ValidationStatusTests
{
    [Fact]
    public void ValidationStatus_ShouldHaveThreeValues()
    {
        // Act
        var values = Enum.GetValues<ValidationStatus>();

        // Assert
        values.Should().HaveCount(3);
        values.Should().Contain(ValidationStatus.Ok);
        values.Should().Contain(ValidationStatus.Warning);
        values.Should().Contain(ValidationStatus.Error);
    }

    [Fact]
    public void ValidationStatus_Ok_ShouldHaveCorrectValue()
    {
        // Assert
        ((int)ValidationStatus.Ok).Should().Be(0);
    }

    [Fact]
    public void ValidationStatus_Warning_ShouldHaveCorrectValue()
    {
        // Assert
        ((int)ValidationStatus.Warning).Should().Be(1);
    }

    [Fact]
    public void ValidationStatus_Error_ShouldHaveCorrectValue()
    {
        // Assert
        ((int)ValidationStatus.Error).Should().Be(2);
    }

    [Theory]
    [InlineData(ValidationStatus.Ok, "Ok")]
    [InlineData(ValidationStatus.Warning, "Warning")]
    [InlineData(ValidationStatus.Error, "Error")]
    public void ToString_ShouldReturnEnumName(ValidationStatus status, string expectedName)
    {
        // Assert
        status.ToString().Should().Be(expectedName);
    }

    [Fact]
    public void ValidationStatus_CanBeCompared()
    {
        // Assert
        (ValidationStatus.Ok < ValidationStatus.Warning).Should().BeTrue();
        (ValidationStatus.Warning < ValidationStatus.Error).Should().BeTrue();
        (ValidationStatus.Ok < ValidationStatus.Error).Should().BeTrue();
    }

    [Theory]
    [InlineData(ValidationStatus.Ok)]
    [InlineData(ValidationStatus.Warning)]
    [InlineData(ValidationStatus.Error)]
    public void ValidationStatus_CanBeUsedInSwitch(ValidationStatus status)
    {
        // Act
        var result = status switch
        {
            ValidationStatus.Ok => "Valid",
            ValidationStatus.Warning => "Warning",
            ValidationStatus.Error => "Invalid",
            _ => throw new ArgumentOutOfRangeException()
        };

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ValidationStatus_CanBeParsed()
    {
        // Act
        var parsedOk = Enum.Parse<ValidationStatus>("Ok");
        var parsedWarning = Enum.Parse<ValidationStatus>("Warning");
        var parsedError = Enum.Parse<ValidationStatus>("Error");

        // Assert
        parsedOk.Should().Be(ValidationStatus.Ok);
        parsedWarning.Should().Be(ValidationStatus.Warning);
        parsedError.Should().Be(ValidationStatus.Error);
    }

    [Fact]
    public void ValidationStatus_TryParse_WithInvalidValue_ShouldReturnFalse()
    {
        // Act
        var result = Enum.TryParse<ValidationStatus>("InvalidStatus", out var status);

        // Assert
        result.Should().BeFalse();
        status.Should().Be(default(ValidationStatus));
    }

    [Fact]
    public void ValidationStatus_IsDefined_ShouldReturnTrueForValidValues()
    {
        // Assert
        Enum.IsDefined(typeof(ValidationStatus), ValidationStatus.Ok).Should().BeTrue();
        Enum.IsDefined(typeof(ValidationStatus), ValidationStatus.Warning).Should().BeTrue();
        Enum.IsDefined(typeof(ValidationStatus), ValidationStatus.Error).Should().BeTrue();
    }

    [Fact]
    public void ValidationStatus_IsDefined_ShouldReturnFalseForInvalidValue()
    {
        // Assert
        Enum.IsDefined(typeof(ValidationStatus), 999).Should().BeFalse();
    }

    [Fact]
    public void ValidationStatus_CanBeUsedInCollections()
    {
        // Arrange
        var statuses = new List<ValidationStatus>
        {
            ValidationStatus.Ok,
            ValidationStatus.Warning,
            ValidationStatus.Error
        };

        // Assert
        statuses.Should().HaveCount(3);
        statuses.Should().ContainInOrder(ValidationStatus.Ok, ValidationStatus.Warning, ValidationStatus.Error);
    }

    [Fact]
    public void ValidationStatus_CanBeUsedAsDictionaryKey()
    {
        // Arrange
        var dictionary = new Dictionary<ValidationStatus, string>
        {
            { ValidationStatus.Ok, "Success" },
            { ValidationStatus.Warning, "Warning" },
            { ValidationStatus.Error, "Failure" }
        };

        // Assert
        dictionary[ValidationStatus.Ok].Should().Be("Success");
        dictionary[ValidationStatus.Warning].Should().Be("Warning");
        dictionary[ValidationStatus.Error].Should().Be("Failure");
    }

    [Fact]
    public void ValidationStatus_GetNames_ShouldReturnAllNames()
    {
        // Act
        var names = Enum.GetNames<ValidationStatus>();

        // Assert
        names.Should().Contain("Ok");
        names.Should().Contain("Warning");
        names.Should().Contain("Error");
        names.Should().HaveCount(3);
    }
}
