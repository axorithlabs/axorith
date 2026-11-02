using System.Globalization;
using Axorith.Sdk.Settings;
using FluentAssertions;
using static Axorith.Sdk.Tests.Settings.SetValueFromStringTests;

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
    public void IntSetting_DeserializeInvalidFormat_ShouldFallBackToDefault(string invalidInput) =>
        SetAndAssert(Setting.AsInt("key", "Label", 999), invalidInput, 999);

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("invalid123")]
    [InlineData("12.34.56")]
    public void DoubleSetting_DeserializeInvalidFormat_ShouldFallBackToDefault(string invalidInput) =>
        SetAndAssert(Setting.AsDouble("key", "Label", 99.9), invalidInput, 99.9);

    [Theory]
    [InlineData("infinity", double.PositiveInfinity)]
    [InlineData("Infinity", double.PositiveInfinity)]
    [InlineData("-infinity", double.NegativeInfinity)]
    [InlineData("NaN", double.NaN)]
    public void DoubleSetting_DeserializeSpecialValues_ShouldParseCorrectly(string input, double expected)
    {
        var setting = Setting.AsDouble("key", "Label", 0.0);

        ((ISetting)setting).SetValueFromString(input);

        setting.GetCurrentValue().Should().Be(expected);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("12.34.56")]
    public void DecimalSetting_DeserializeInvalidFormat_ShouldFallBackToDefault(string invalidInput) =>
        SetAndAssert(Setting.AsNumber("key", "Label", 100m), invalidInput, 100m);

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("random")]
    public void BoolSetting_DeserializeInvalidFormat_ShouldBeFalse(string invalidInput) =>
        SetAndAssert(Setting.AsCheckbox("key", "Label", true), invalidInput, false);

    [Fact]
    public void TimeSpanSetting_DeserializeInvalidSeconds_ShouldFallBackToDefault() =>
        SetAndAssert(Setting.AsTimeSpan("key", "Label", TimeSpan.FromMinutes(5)),
            "not-a-number", TimeSpan.FromMinutes(5));

    [Fact]
    public void ISetting_SetValueFromObject_WithIncompatibleType_ShouldNotCrash()
    {
        var setting = Setting.AsInt("key", "Label", 0);
        setting.SetValue(123);

        var act = () => ((ISetting)setting).SetValueFromObject(new object());

        act.Should().NotThrow();
        setting.GetCurrentValue().Should().Be(123);
    }

    [Fact]
    public void ISetting_SetValueFromObject_WithNull_ShouldHandleGracefully()
    {
        var setting = Setting.AsText("key", "Label", "default");
        setting.SetValue("changed");

        ((ISetting)setting).SetValueFromObject(null);

        setting.GetCurrentValue().Should().Be("changed");
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
        var longString = new string('X', 1_000_000); // 1MB string

        var act = () => ((ISetting)setting).SetValueFromString(longString);

        act.Should().NotThrow();
        setting.GetCurrentValue().Should().HaveLength(1_000_000);
    }

    [Fact]
    public void IntSetting_SetValueFromObject_WithDouble_ShouldConvert()
    {
        var setting = Setting.AsInt("key", "Label", 0);

        ((ISetting)setting).SetValueFromObject(42.7);

        setting.GetCurrentValue().Should().Be(43);
    }

    [Fact]
    public void DoubleSetting_SetValueFromObject_WithInt_ShouldConvert()
    {
        var setting = Setting.AsDouble("key", "Label", 0.0);

        ((ISetting)setting).SetValueFromObject(42);

        setting.GetCurrentValue().Should().Be(42.0);
    }

    [Theory]
    [InlineData(60.0, 60.0)]
    [InlineData(-1.5, -1.5)]
    [InlineData(42, 42.0)]
    public void TimeSpanSetting_SetValueFromObject_WithConvertible_ShouldInterpretAsSeconds(object input, double seconds)
    {
        using var setting = Setting.AsTimeSpan("key", "Label", TimeSpan.FromHours(1));

        ((ISetting)setting).SetValueFromObject(input);

        setting.GetCurrentValue().Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void TextSetting_SetValueFromString_WithNull_ShouldUseDefault() =>
        SetAndAssert(Setting.AsText("key", "Label", "default"), null, "default");

    [Fact]
    public void SecretSetting_SetValueFromString_WithNull_ShouldUseEmptyString() =>
        SetAndAssert(Setting.AsSecret("key", "Label"), null, string.Empty);

    [Fact]
    public void Setting_GetValueAsString_AfterMultipleUpdates_ShouldReturnLatest()
    {
        var setting = Setting.AsInt("key", "Label", 0);

        for (var i = 1; i <= 100; i++) setting.SetValue(i);
        var result = ((ISetting)setting).GetValueAsString();

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
