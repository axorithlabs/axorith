using System.Globalization;
using Axorith.Sdk.Settings;
using FluentAssertions;

namespace Axorith.Sdk.Tests.Settings;

/// <summary>
///     Tests for setting serialization and deserialization
/// </summary>
public class SettingSerializationTests
{
    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("", "")]
    [InlineData("special chars: \n\t\r", "special chars: \n\t\r")]
    public void TextSetting_Serialization_ShouldPreserveValue(string value, string expected) =>
        RoundTrip(Setting.AsText("key", "Label", "default"),
            Setting.AsText("key", "Label", "different initial value"), value).Should().Be(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(-100)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void IntSetting_Serialization_ShouldPreserveValue(int value) =>
        RoundTrip(Setting.AsInt("key", "Label", 0),
            Setting.AsInt("key", "Label", value == 0 ? 1 : 0), value).Should().Be(value);

    [Theory]
    [InlineData(0.0)]
    [InlineData(3.14159)]
    [InlineData(-2.71828)]
    [InlineData(1.234567890123456)]
    public void DoubleSetting_Serialization_ShouldPreserveValue(double value) =>
        RoundTrip(Setting.AsDouble("key", "Label", 0),
            Setting.AsDouble("key", "Label", value == 0 ? 1 : 0), value).Should().BeApproximately(value, 1e-10);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BoolSetting_Serialization_ShouldPreserveValue(bool value) =>
        RoundTrip(Setting.AsCheckbox("key", "Label", false),
            Setting.AsCheckbox("key", "Label", !value), value).Should().Be(value);

    [Fact]
    public void TimeSpanSetting_Serialization_ShouldPreserveDuration() =>
        RoundTrip(Setting.AsTimeSpan("key", "Label", TimeSpan.Zero),
            Setting.AsTimeSpan("key", "Label", TimeSpan.Zero), TimeSpan.FromMinutes(123.456))
            .Should().BeCloseTo(TimeSpan.FromMinutes(123.456), TimeSpan.FromMilliseconds(1));

    [Fact]
    public void DecimalSetting_Serialization_WithInvariantCulture_ShouldWork()
    {
        var currentCulture = CultureInfo.CurrentCulture;
        try
        {
            // Set culture with different decimal separator
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU"); // Uses comma as decimal separator

            var setting = Setting.AsNumber("key", "Label", 0m);
            var iSetting = (ISetting)setting;
            setting.SetValue(123.45m);

            var serialized = iSetting.GetValueAsString();
            using var restored = Setting.AsNumber("key", "Label", 0m);
            ((ISetting)restored).SetValueFromString(serialized);

            restored.GetCurrentValue().Should().Be(123.45m);
            serialized.Should().Contain("."); // Should use invariant culture (dot)
        }
        finally
        {
            CultureInfo.CurrentCulture = currentCulture;
        }
    }

    [Fact]
    public void ChoiceSetting_Serialization_ShouldPreserveSelectedKey()
    {
        var choices = new List<KeyValuePair<string, string>>
        {
            new("key1", "Value 1"),
            new("key2", "Value 2"),
            new("key3", "Value 3")
        };
        var setting = Setting.AsChoice("choice", "Choice", "key1", choices);
        setting.SetValue("key2");

        var serialized = ((ISetting)setting).GetValueAsString();
        using var restored = Setting.AsChoice("choice", "Choice", "key1", choices);
        ((ISetting)restored).SetValueFromString(serialized);

        restored.GetCurrentValue().Should().Be("key2");
        serialized.Should().Be("key2"); // Should serialize the key, not the display value
    }

    [Fact]
    public void MultiChoiceRoundTripPreservesSelectedKeysAndTheirOrderInANewSetting()
    {
        var choices = new[] { KeyValuePair.Create("a", "First"), KeyValuePair.Create("b", "Second"),
            KeyValuePair.Create("c", "Third") };
        using var source = Setting.AsMultiChoice("categories", "Categories", ["a"], choices);
        using var restored = Setting.AsMultiChoice("categories", "Categories", ["a"], choices);
        source.SetValue(["c", "b"]);

        ((ISetting)restored).SetValueFromString(((ISetting)source).GetValueAsString());

        restored.GetCurrentValue().Should().Equal("c", "b");
        restored.ControlType.Should().Be(SettingControlType.MultiChoice);
        ((ISetting)restored).GetCurrentChoices().Should().Equal(choices);
        source.SetValue([]);
        ((ISetting)restored).SetValueFromString(((ISetting)source).GetValueAsString());
        restored.GetCurrentValue().Should().BeEmpty();
    }

    [Fact]
    public void TextSetting_DeserializeNull_ShouldUseDefault()
    {
        var setting = Setting.AsText("key", "Label", "default");
        setting.SetValue("changed");

        ((ISetting)setting).SetValueFromString(null);

        setting.GetCurrentValue().Should().Be("default");
    }

    [Fact]
    public void TextSetting_DeserializeEmptyString_ShouldPreserveEmptyString()
    {
        var setting = Setting.AsText("key", "Label", "default");

        ((ISetting)setting).SetValueFromString("");

        setting.GetCurrentValue().Should().Be("", "empty string is a valid value distinct from null");
    }

    [Theory]
    [InlineData("not a number")]
    [InlineData("")]
    [InlineData(null)]
    public void IntSetting_DeserializeInvalid_ShouldUseDefault(string? input)
    {
        var setting = Setting.AsInt("key", "Label", 999);
        setting.SetValue(123);

        ((ISetting)setting).SetValueFromString(input);

        setting.GetCurrentValue().Should().Be(999);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("1")]
    [InlineData("0")]
    public void BoolSetting_DeserializeInvalid_ShouldBeFalse(string input)
    {
        var setting = Setting.AsCheckbox("key", "Label", true);

        ((ISetting)setting).SetValueFromString(input);

        // Invalid bool strings parse to false
        setting.GetCurrentValue().Should().BeFalse();
    }

    [Fact]
    public void MultipleSettings_SerializeAndDeserialize_ShouldMaintainIndependence()
    {
        var text = Setting.AsText("text", "Text", "default");
        var number = Setting.AsInt("number", "Number", 0);
        var checkbox = Setting.AsCheckbox("check", "Check", false);

        var iText = (ISetting)text;
        var iNumber = (ISetting)number;
        var iCheck = (ISetting)checkbox;

        text.SetValue("Hello");
        number.SetValue(42);
        checkbox.SetValue(true);

        var textSerialized = iText.GetValueAsString();
        var numberSerialized = iNumber.GetValueAsString();
        var checkSerialized = iCheck.GetValueAsString();

        // Create new settings and deserialize
        var text2 = Setting.AsText("text", "Text", "");
        var number2 = Setting.AsInt("number", "Number", 0);
        var checkbox2 = Setting.AsCheckbox("check", "Check", false);

        ((ISetting)text2).SetValueFromString(textSerialized);
        ((ISetting)number2).SetValueFromString(numberSerialized);
        ((ISetting)checkbox2).SetValueFromString(checkSerialized);

        text2.GetCurrentValue().Should().Be("Hello");
        number2.GetCurrentValue().Should().Be(42);
        checkbox2.GetCurrentValue().Should().BeTrue();
    }
    private static T RoundTrip<T>(Setting<T> source, Setting<T> target, T value)
    {
        using (source)
        using (target)
        {
            target.GetCurrentValue().Should().NotBe(value);
            source.SetValue(value);
            ((ISetting)target).SetValueFromString(((ISetting)source).GetValueAsString());
            return target.GetCurrentValue();
        }
    }

}
