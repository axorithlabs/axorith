using System.Globalization;
using Axorith.Sdk.Settings;
using FluentAssertions;

namespace Axorith.Sdk.Tests.Settings;

/// <summary>
///     Tests for error handling in serialization/deserialization and type conversion
/// </summary>
public class SettingErrorHandlingTests
{
    [Theory]
    [InlineData("not-a-number")]
    [InlineData("abc123")]
    [InlineData("12.34.56")]
    [InlineData("")]
    public void IntSetting_DeserializeInvalidFormat_ShouldFallBackToDefault(string invalidInput)
    {
        // Arrange
        var defaultValue = 999;
        var setting = Setting.AsInt("key", "Label", defaultValue);
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromString(invalidInput);

        // Assert
        setting.GetCurrentValue().Should().Be(defaultValue);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("invalid123")]
    [InlineData("12.34.56")]
    public void DoubleSetting_DeserializeInvalidFormat_ShouldFallBackToDefault(string invalidInput)
    {
        // Arrange
        var defaultValue = 99.9;
        var setting = Setting.AsDouble("key", "Label", defaultValue);
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromString(invalidInput);

        // Assert
        setting.GetCurrentValue().Should().Be(defaultValue);
    }

    [Theory]
    [InlineData("infinity", double.PositiveInfinity)]
    [InlineData("Infinity", double.PositiveInfinity)]
    [InlineData("-infinity", double.NegativeInfinity)]
    [InlineData("NaN", double.NaN)]
    public void DoubleSetting_DeserializeSpecialValues_ShouldParseCorrectly(string input, double expected)
    {
        // Arrange
        var setting = Setting.AsDouble("key", "Label", 0.0);
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromString(input);

        // Assert
        // Special doubles require special comparison
        if (double.IsNaN(expected))
        {
            setting.GetCurrentValue().Should().Be(double.NaN);
        }
        else if (double.IsPositiveInfinity(expected))
        {
            setting.GetCurrentValue().Should().Be(double.PositiveInfinity);
        }
        else if (double.IsNegativeInfinity(expected))
        {
            setting.GetCurrentValue().Should().Be(double.NegativeInfinity);
        }
        else
        {
            setting.GetCurrentValue().Should().Be(expected);
        }
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("12.34.56")]
    public void DecimalSetting_DeserializeInvalidFormat_ShouldFallBackToDefault(string invalidInput)
    {
        // Arrange
        var defaultValue = 100m;
        var setting = Setting.AsNumber("key", "Label", defaultValue);
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromString(invalidInput);

        // Assert
        setting.GetCurrentValue().Should().Be(defaultValue);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("random")]
    public void BoolSetting_DeserializeInvalidFormat_ShouldBeFalse(string invalidInput)
    {
        // Arrange
        var setting = Setting.AsCheckbox("key", "Label", true);
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromString(invalidInput);

        // Assert - bool.TryParse returns false for invalid strings
        setting.GetCurrentValue().Should().BeFalse();
    }

    [Fact]
    public void TimeSpanSetting_DeserializeInvalidSeconds_ShouldFallBackToDefault()
    {
        // Arrange
        var defaultValue = TimeSpan.FromMinutes(5);
        var setting = Setting.AsTimeSpan("key", "Label", defaultValue);
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromString("not-a-number");

        // Assert
        setting.GetCurrentValue().Should().Be(defaultValue);
    }

    [Fact]
    public void ISetting_SetValueFromObject_WithIncompatibleType_ShouldNotCrash()
    {
        // Arrange
        var setting = Setting.AsInt("key", "Label", 0);
        var iSetting = (ISetting)setting;

        // Act - try to set incompatible object
        var act = () => iSetting.SetValueFromObject(new object());

        // Assert - should not throw, just ignore or handle gracefully
        act.Should().NotThrow();
    }

    [Fact]
    public void ISetting_SetValueFromObject_WithNull_ShouldHandleGracefully()
    {
        // Arrange
        var setting = Setting.AsText("key", "Label", "default");
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromObject(null);

        // Assert - should fall back to deserializer with null
        setting.GetCurrentValue().Should().Be("default");
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ru-RU")]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    public void NumberSetting_WithDifferentCultures_ShouldUseInvariantCulture(string cultureName)
    {
        // Arrange
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            var setting = Setting.AsNumber("key", "Label", 0m);
            var iSetting = (ISetting)setting;
            setting.SetValue(123.45m);

            // Act
            var serialized = iSetting.GetValueAsString();

            // Reset setting
            var setting2 = Setting.AsNumber("key2", "Label2", 0m);
            var iSetting2 = (ISetting)setting2;
            iSetting2.SetValueFromString(serialized);

            // Assert
            serialized.Should().Contain("."); // Should use dot, not comma
            setting2.GetCurrentValue().Should().Be(123.45m);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void ISetting_SetValueFromString_WithExtremelyLongString_ShouldHandle()
    {
        // Arrange
        var setting = Setting.AsText("key", "Label", "default");
        var iSetting = (ISetting)setting;
        var longString = new string('X', 1_000_000); // 1MB string

        // Act
        var act = () => iSetting.SetValueFromString(longString);

        // Assert
        act.Should().NotThrow();
        setting.GetCurrentValue().Should().HaveLength(1_000_000);
    }

    [Fact]
    public void IntSetting_SetValueFromObject_WithDouble_ShouldConvert()
    {
        // Arrange
        var setting = Setting.AsInt("key", "Label", 0);
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromObject(42.7);

        // Assert - rounds to nearest
        setting.GetCurrentValue().Should().Be(43);
    }

    [Fact]
    public void DoubleSetting_SetValueFromObject_WithInt_ShouldConvert()
    {
        // Arrange
        var setting = Setting.AsDouble("key", "Label", 0.0);
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromObject(42);

        // Assert
        setting.GetCurrentValue().Should().Be(42.0);
    }

    [Fact]
    public void TimeSpanSetting_SetValueFromObject_WithDouble_ShouldInterpretAsSeconds()
    {
        // Arrange
        var setting = Setting.AsTimeSpan("key", "Label", TimeSpan.Zero);
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromObject(60.0); // 60 seconds

        // Assert
        setting.GetCurrentValue().Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void TextSetting_SetValueFromString_WithNull_ShouldUseDefault()
    {
        // Arrange
        var setting = Setting.AsText("key", "Label", "default");
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromString(null);

        // Assert
        setting.GetCurrentValue().Should().Be("default");
    }

    [Fact]
    public void SecretSetting_SetValueFromString_WithNull_ShouldUseEmptyString()
    {
        // Arrange
        var setting = Setting.AsSecret("key", "Label");
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromString(null);

        // Assert
        setting.GetCurrentValue().Should().BeEmpty();
    }

    [Fact]
    public void Setting_GetValueAsString_AfterMultipleUpdates_ShouldReturnLatest()
    {
        // Arrange
        var setting = Setting.AsInt("key", "Label", 0);
        var iSetting = (ISetting)setting;

        // Act
        for (var i = 1; i <= 100; i++) setting.SetValue(i);
        var result = iSetting.GetValueAsString();

        // Assert
        result.Should().Be("100");
    }

    [Fact]
    public void ChoiceSetting_SetValue_WithKeyNotInChoices_ShouldStillAccept()
    {
        // Arrange
        var choices = new List<KeyValuePair<string, string>>
        {
            new("a", "A"),
            new("b", "B")
        };
        var setting = Setting.AsChoice("choice", "Choice", "a", choices);

        // Act - set value that's not in choices
        setting.SetValue("z");

        // Assert - SDK doesn't validate, modules should handle this
        setting.GetCurrentValue().Should().Be("z");
    }
}