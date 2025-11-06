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
    public void TextSetting_Serialization_ShouldPreserveValue(string value, string expected)
    {
        // Arrange
        var setting = Setting.AsText("key", "Label", "default");
        var iSetting = (ISetting)setting;
        setting.SetValue(value);

        // Act
        var serialized = iSetting.GetValueAsString();
        iSetting.SetValueFromString(serialized);

        // Assert
        setting.GetCurrentValue().Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(-100)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void IntSetting_Serialization_ShouldPreserveValue(int value)
    {
        // Arrange
        var setting = Setting.AsInt("key", "Label", 0);
        var iSetting = (ISetting)setting;
        setting.SetValue(value);

        // Act
        var serialized = iSetting.GetValueAsString();
        iSetting.SetValueFromString(serialized);

        // Assert
        setting.GetCurrentValue().Should().Be(value);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(3.14159)]
    [InlineData(-2.71828)]
    [InlineData(1.234567890123456)]
    public void DoubleSetting_Serialization_ShouldPreserveValue(double value)
    {
        // Arrange
        var setting = Setting.AsDouble("key", "Label", 0.0);
        var iSetting = (ISetting)setting;
        setting.SetValue(value);

        // Act
        var serialized = iSetting.GetValueAsString();
        iSetting.SetValueFromString(serialized);

        // Assert
        setting.GetCurrentValue().Should().BeApproximately(value, 1e-10);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BoolSetting_Serialization_ShouldPreserveValue(bool value)
    {
        // Arrange
        var setting = Setting.AsCheckbox("key", "Label", false);
        var iSetting = (ISetting)setting;
        setting.SetValue(value);

        // Act
        var serialized = iSetting.GetValueAsString();
        iSetting.SetValueFromString(serialized);

        // Assert
        setting.GetCurrentValue().Should().Be(value);
    }

    [Fact]
    public void TimeSpanSetting_Serialization_ShouldPreserveDuration()
    {
        // Arrange
        var duration = TimeSpan.FromMinutes(123.456);
        var setting = Setting.AsTimeSpan("key", "Label", TimeSpan.Zero);
        var iSetting = (ISetting)setting;
        setting.SetValue(duration);

        // Act
        var serialized = iSetting.GetValueAsString();
        iSetting.SetValueFromString(serialized);

        // Assert
        setting.GetCurrentValue().Should().BeCloseTo(duration, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void DecimalSetting_Serialization_WithInvariantCulture_ShouldWork()
    {
        // Arrange
        var currentCulture = CultureInfo.CurrentCulture;
        try
        {
            // Set culture with different decimal separator
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU"); // Uses comma as decimal separator

            var setting = Setting.AsNumber("key", "Label", 0m);
            var iSetting = (ISetting)setting;
            setting.SetValue(123.45m);

            // Act
            var serialized = iSetting.GetValueAsString();
            iSetting.SetValueFromString(serialized);

            // Assert
            setting.GetCurrentValue().Should().Be(123.45m);
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
        // Arrange
        var choices = new List<KeyValuePair<string, string>>
        {
            new("key1", "Value 1"),
            new("key2", "Value 2"),
            new("key3", "Value 3")
        };
        var setting = Setting.AsChoice("choice", "Choice", "key1", choices);
        var iSetting = (ISetting)setting;
        setting.SetValue("key2");

        // Act
        var serialized = iSetting.GetValueAsString();
        iSetting.SetValueFromString(serialized);

        // Assert
        setting.GetCurrentValue().Should().Be("key2");
        serialized.Should().Be("key2"); // Should serialize the key, not the display value
    }

    [Fact]
    public void TextSetting_DeserializeNull_ShouldUseDefault()
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
    public void TextSetting_DeserializeEmptyString_ShouldPreserveEmptyString()
    {
        // Arrange
        var setting = Setting.AsText("key", "Label", "default");
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromString("");

        // Assert
        setting.GetCurrentValue().Should().Be("", "empty string is a valid value distinct from null");
    }

    [Theory]
    [InlineData("not a number")]
    [InlineData("")]
    [InlineData(null)]
    public void IntSetting_DeserializeInvalid_ShouldUseDefault(string? input)
    {
        // Arrange
        var setting = Setting.AsInt("key", "Label", 999);
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromString(input);

        // Assert
        setting.GetCurrentValue().Should().Be(999);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("1")]
    [InlineData("0")]
    public void BoolSetting_DeserializeInvalid_ShouldBeFalse(string input)
    {
        // Arrange
        var setting = Setting.AsCheckbox("key", "Label", true);
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromString(input);

        // Assert
        // Invalid bool strings parse to false
        setting.GetCurrentValue().Should().BeFalse();
    }

    [Fact]
    public void MultipleSettings_SerializeAndDeserialize_ShouldMaintainIndependence()
    {
        // Arrange
        var text = Setting.AsText("text", "Text", "default");
        var number = Setting.AsInt("number", "Number", 0);
        var checkbox = Setting.AsCheckbox("check", "Check", false);

        var iText = (ISetting)text;
        var iNumber = (ISetting)number;
        var iCheck = (ISetting)checkbox;

        text.SetValue("Hello");
        number.SetValue(42);
        checkbox.SetValue(true);

        // Act
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

        // Assert
        text2.GetCurrentValue().Should().Be("Hello");
        number2.GetCurrentValue().Should().Be(42);
        checkbox2.GetCurrentValue().Should().BeTrue();
    }
}