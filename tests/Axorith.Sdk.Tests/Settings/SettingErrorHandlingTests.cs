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
        var defaultValue = 999;
        var setting = Setting.AsInt("key", "Label", defaultValue);
        var iSetting = (ISetting)setting;

        iSetting.SetValueFromString(invalidInput);

        setting.GetCurrentValue().Should().Be(defaultValue);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("invalid123")]
    [InlineData("12.34.56")]
    public void DoubleSetting_DeserializeInvalidFormat_ShouldFallBackToDefault(string invalidInput)
    {
        var defaultValue = 99.9;
        var setting = Setting.AsDouble("key", "Label", defaultValue);
        var iSetting = (ISetting)setting;

        iSetting.SetValueFromString(invalidInput);

        setting.GetCurrentValue().Should().Be(defaultValue);
    }

    [Theory]
    [InlineData("infinity", double.PositiveInfinity)]
    [InlineData("Infinity", double.PositiveInfinity)]
    [InlineData("-infinity", double.NegativeInfinity)]
    [InlineData("NaN", double.NaN)]
    public void DoubleSetting_DeserializeSpecialValues_ShouldParseCorrectly(string input, double expected)
    {
        var setting = Setting.AsDouble("key", "Label", 0.0);
        var iSetting = (ISetting)setting;

        iSetting.SetValueFromString(input);

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
        var defaultValue = 100m;
        var setting = Setting.AsNumber("key", "Label", defaultValue);
        var iSetting = (ISetting)setting;

        iSetting.SetValueFromString(invalidInput);

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
        var setting = Setting.AsCheckbox("key", "Label", true);
        var iSetting = (ISetting)setting;

        iSetting.SetValueFromString(invalidInput);

        setting.GetCurrentValue().Should().BeFalse();
    }

    [Fact]
    public void TimeSpanSetting_DeserializeInvalidSeconds_ShouldFallBackToDefault()
    {
        var defaultValue = TimeSpan.FromMinutes(5);
        var setting = Setting.AsTimeSpan("key", "Label", defaultValue);
        var iSetting = (ISetting)setting;

        iSetting.SetValueFromString("not-a-number");

        setting.GetCurrentValue().Should().Be(defaultValue);
    }

    [Fact]
    public void ISetting_SetValueFromObject_WithIncompatibleType_ShouldNotCrash()
    {
        var setting = Setting.AsInt("key", "Label", 0);
        var iSetting = (ISetting)setting;

        var act = () => iSetting.SetValueFromObject(new object());

        act.Should().NotThrow();
    }

    [Fact]
    public void ISetting_SetValueFromObject_WithNull_ShouldHandleGracefully()
    {
        var setting = Setting.AsText("key", "Label", "default");
        var iSetting = (ISetting)setting;

        iSetting.SetValueFromObject(null);

        setting.GetCurrentValue().Should().Be("default");
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ru-RU")]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    public void NumberSetting_WithDifferentCultures_ShouldUseInvariantCulture(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            var setting = Setting.AsNumber("key", "Label", 0m);
            var iSetting = (ISetting)setting;
            setting.SetValue(123.45m);

            var serialized = iSetting.GetValueAsString();

            // Reset setting
            var setting2 = Setting.AsNumber("key2", "Label2", 0m);
            var iSetting2 = (ISetting)setting2;
            iSetting2.SetValueFromString(serialized);

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
        var setting = Setting.AsText("key", "Label", "default");
        var iSetting = (ISetting)setting;
        var longString = new string('X', 1_000_000); // 1MB string

        var act = () => iSetting.SetValueFromString(longString);

        act.Should().NotThrow();
        setting.GetCurrentValue().Should().HaveLength(1_000_000);
    }

    [Fact]
    public void IntSetting_SetValueFromObject_WithDouble_ShouldConvert()
    {
        var setting = Setting.AsInt("key", "Label", 0);
        var iSetting = (ISetting)setting;

        iSetting.SetValueFromObject(42.7);

        setting.GetCurrentValue().Should().Be(43);
    }

    [Fact]
    public void DoubleSetting_SetValueFromObject_WithInt_ShouldConvert()
    {
        var setting = Setting.AsDouble("key", "Label", 0.0);
        var iSetting = (ISetting)setting;

        iSetting.SetValueFromObject(42);

        setting.GetCurrentValue().Should().Be(42.0);
    }

    [Fact]
    public void TimeSpanSetting_SetValueFromObject_WithDouble_ShouldInterpretAsSeconds()
    {
        var setting = Setting.AsTimeSpan("key", "Label", TimeSpan.Zero);
        var iSetting = (ISetting)setting;

        iSetting.SetValueFromObject(60.0); // 60 seconds

        setting.GetCurrentValue().Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void TextSetting_SetValueFromString_WithNull_ShouldUseDefault()
    {
        var setting = Setting.AsText("key", "Label", "default");
        var iSetting = (ISetting)setting;

        iSetting.SetValueFromString(null);

        setting.GetCurrentValue().Should().Be("default");
    }

    [Fact]
    public void SecretSetting_SetValueFromString_WithNull_ShouldUseEmptyString()
    {
        var setting = Setting.AsSecret("key", "Label");
        var iSetting = (ISetting)setting;

        iSetting.SetValueFromString(null);

        setting.GetCurrentValue().Should().BeEmpty();
    }

    [Fact]
    public void Setting_GetValueAsString_AfterMultipleUpdates_ShouldReturnLatest()
    {
        var setting = Setting.AsInt("key", "Label", 0);
        var iSetting = (ISetting)setting;

        for (var i = 1; i <= 100; i++) setting.SetValue(i);
        var result = iSetting.GetValueAsString();

        result.Should().Be("100");
    }

    [Fact]
    public void ChoiceSetting_SetValue_WithKeyNotInChoices_ShouldStillAccept()
    {
        var choices = new List<KeyValuePair<string, string>>
        {
            new("a", "A"),
            new("b", "B")
        };
        var setting = Setting.AsChoice("choice", "Choice", "a", choices);

        setting.SetValue("z");

        setting.GetCurrentValue().Should().Be("z");
    }
}
