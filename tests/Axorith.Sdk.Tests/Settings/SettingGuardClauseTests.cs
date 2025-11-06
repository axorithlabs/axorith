using System.Reflection;
using Axorith.Sdk.Settings;
using FluentAssertions;

namespace Axorith.Sdk.Tests.Settings;

/// <summary>
///     Tests for new guard clauses added to Setting
///     Validates ArgumentException.ThrowIfNullOrWhiteSpace and ArgumentNullException.ThrowIfNull
/// </summary>
public class SettingGuardClauseTests
{
    #region Constructor Validation

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AsText_WithInvalidKey_ShouldThrow(string? invalidKey)
    {
        // Act
        var act = () => Setting.AsText(invalidKey!, "Label", "value");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("key");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AsText_WithInvalidLabel_ShouldThrow(string? invalidLabel)
    {
        // Act
        var act = () => Setting.AsText("key", invalidLabel!, "value");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("label");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AsCheckbox_WithInvalidKey_ShouldThrow(string? invalidKey)
    {
        // Act
        var act = () => Setting.AsCheckbox(invalidKey!, "Label", false);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AsNumber_WithInvalidKey_ShouldThrow(string? invalidKey)
    {
        // Act
        var act = () => Setting.AsNumber(invalidKey!, "Label", 0m);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AsChoice_WithInvalidKey_ShouldThrow(string? invalidKey)
    {
        // Arrange
        var choices = new List<KeyValuePair<string, string>> { new("k", "v") };

        // Act
        var act = () => Setting.AsChoice(invalidKey!, "Label", "k", choices);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region SetLabel Validation

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetLabel_WithInvalidLabel_ShouldThrow(string? invalidLabel)
    {
        // Arrange
        var setting = Setting.AsText("key", "Label", "value");

        // Act
        var act = () => setting.SetLabel(invalidLabel!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("newLabel");
    }

    #endregion

    #region SetChoices Validation

    [Fact]
    public void SetChoices_WithNull_ShouldThrow()
    {
        // Arrange
        var choices = new List<KeyValuePair<string, string>> { new("k", "v") };
        var setting = Setting.AsChoice("choice", "Choice", "k", choices);

        // Act
        var act = () => setting.SetChoices(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("newChoices");
    }

    [Fact]
    public void SetChoices_WithEmptyList_ShouldNotThrow()
    {
        // Arrange
        var choices = new List<KeyValuePair<string, string>> { new("k", "v") };
        var setting = Setting.AsChoice("choice", "Choice", "k", choices);

        // Act
        var act = () => setting.SetChoices([]);

        // Assert - this is allowed now (critical requirement from user)
        act.Should().NotThrow();
    }

    [Fact]
    public void SetChoices_WithNullKeyInList_ShouldNotThrowButAcceptIt()
    {
        // Arrange
        var choices = new List<KeyValuePair<string, string>> { new("k", "v") };
        var setting = Setting.AsChoice("choice", "Choice", "k", choices);

        // Act
        var newChoices = new List<KeyValuePair<string, string>>
        {
            new(null!, "Null Key")
        };

        var act = () => setting.SetChoices(newChoices);

        // Assert - SDK doesn't validate individual items, that's module responsibility
        act.Should().NotThrow();
    }

    [Fact]
    public void SetChoices_WithDuplicateKeys_ShouldNotThrowButAcceptThem()
    {
        // Arrange
        var choices = new List<KeyValuePair<string, string>> { new("k", "v") };
        var setting = Setting.AsChoice("choice", "Choice", "k", choices);

        // Act
        var newChoices = new List<KeyValuePair<string, string>>
        {
            new("duplicate", "First"),
            new("duplicate", "Second")
        };

        var act = () => setting.SetChoices(newChoices);

        // Assert - SDK doesn't prevent duplicates
        act.Should().NotThrow();
    }

    #endregion

    #region InitializeChoices Validation

    [Fact]
    public void InitializeChoices_WithNull_ShouldThrow()
    {
        // This tests internal method through AsChoice
        // Arrange & Act
        var act = () =>
        {
            // Use reflection to call internal method
            var setting = Setting.AsText("key", "Label", "value");
            var method = typeof(Setting<string>).GetMethod("InitializeChoices",
                BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(setting, [null]);
        };

        // Assert
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void AsChoice_WithKeyNotInChoices_ShouldStillCreate()
    {
        // Arrange
        var choices = new List<KeyValuePair<string, string>>
        {
            new("a", "A"),
            new("b", "B")
        };

        // Act - default value "c" is not in choices
        var setting = Setting.AsChoice("choice", "Choice", "c", choices);

        // Assert
        setting.GetCurrentValue().Should().Be("c");
    }

    [Fact]
    public void MultipleSettings_WithSameKey_ShouldBeIndependent()
    {
        // Arrange & Act
        var setting1 = Setting.AsText("same-key", "Label 1", "value1");
        var setting2 = Setting.AsText("same-key", "Label 2", "value2");

        // Assert
        setting1.Key.Should().Be("same-key");
        setting2.Key.Should().Be("same-key");
        setting1.GetCurrentValue().Should().Be("value1");
        setting2.GetCurrentValue().Should().Be("value2");

        // Changing one should not affect the other
        setting1.SetValue("changed");
        setting2.GetCurrentValue().Should().Be("value2");
    }

    [Fact]
    public void Setting_WithVeryLongKey_ShouldWork()
    {
        // Arrange
        var longKey = new string('k', 1000);

        // Act
        var act = () => Setting.AsText(longKey, "Label", "value");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Setting_WithUnicodeKey_ShouldWork()
    {
        // Arrange
        var unicodeKey = "キー_клавиша_🔑";

        // Act
        var setting = Setting.AsText(unicodeKey, "Label", "value");

        // Assert
        setting.Key.Should().Be(unicodeKey);
    }

    [Fact]
    public void Setting_WithSpecialCharactersInKey_ShouldWork()
    {
        // Arrange
        var specialKey = "key.with-special_chars@123";

        // Act
        var setting = Setting.AsText(specialKey, "Label", "value");

        // Assert
        setting.Key.Should().Be(specialKey);
    }

    #endregion
}