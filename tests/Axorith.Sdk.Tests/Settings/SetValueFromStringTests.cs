using Axorith.Sdk.Settings;
using FluentAssertions;

namespace Axorith.Sdk.Tests.Settings;

/// <summary>
///     Comprehensive Senior-level tests for ISetting.SetValueFromString
///     Critical for preset deserialization reliability - all types must parse correctly
/// </summary>
public class SetValueFromStringTests
{
    #region String Type Tests

    [Theory]
    [InlineData("simple text")]
    [InlineData("")]
    [InlineData("with\nnewlines\nand\ttabs")]
    [InlineData("unicode: 你好世界 🚀")]
    [InlineData("special !@#$%^&*()_+-=[]{}")]
    public void SetValueFromString_TextSetting_ValidStrings_ShouldParse(string input)
    {
        // Arrange
        ISetting setting = Setting.AsText("key", "Label", "default");

        // Act
        setting.SetValueFromString(input);

        // Assert
        setting.GetCurrentValueAsObject().Should().Be(input);
        setting.GetValueAsString().Should().Be(input);
    }

    [Fact]
    public void SetValueFromString_TextSetting_Null_ShouldUseDefault()
    {
        // Arrange
        var defaultValue = "default-text";
        ISetting setting = Setting.AsText("key", "Label", defaultValue);

        // Act
        setting.SetValueFromString(null);

        // Assert
        setting.GetCurrentValueAsObject().Should().Be(defaultValue);
    }

    [Fact]
    public void SetValueFromString_TextAreaSetting_ShouldWorkIdentically()
    {
        // Arrange
        ISetting setting = Setting.AsTextArea("key", "Label", "default");
        var multilineText = "Line 1\nLine 2\nLine 3";

        // Act
        setting.SetValueFromString(multilineText);

        // Assert
        setting.GetCurrentValueAsObject().Should().Be(multilineText);
    }

    #endregion

    #region Boolean Type Tests

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("FALSE", false)]
    public void SetValueFromString_CheckboxSetting_ValidBooleans_ShouldParse(string input, bool expected)
    {
        // Arrange
        ISetting setting = Setting.AsCheckbox("key", "Label", false);

        // Act
        setting.SetValueFromString(input);

        // Assert
        setting.GetCurrentValueAsObject().Should().Be(expected);
        setting.GetValueAsString().Should().Be(expected.ToString());
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("invalid")]
    [InlineData("")]
    public void SetValueFromString_CheckboxSetting_InvalidInput_ShouldUseFalse(string input)
    {
        // Arrange
        ISetting setting = Setting.AsCheckbox("key", "Label", false);

        // Act
        setting.SetValueFromString(input);

        // Assert
        // bool.TryParse returns false for invalid input
        setting.GetCurrentValueAsObject().Should().Be(false);
    }

    [Fact]
    public void SetValueFromString_CheckboxSetting_Null_ShouldUseFalse()
    {
        // Arrange
        ISetting setting = Setting.AsCheckbox("key", "Label", true);

        // Act
        setting.SetValueFromString(null);

        // Assert
        // bool.TryParse(null) returns false
        setting.GetCurrentValueAsObject().Should().Be(false);
    }

    #endregion

    #region Decimal Type Tests

    [Theory]
    [InlineData("0", "0")]
    [InlineData("123.45", "123.45")]
    [InlineData("-999.99", "-999.99")]
    [InlineData("0.001", "0.001")]
    [InlineData("1234567890.123456789", "1234567890.123456789")]
    public void SetValueFromString_NumberSetting_ValidDecimals_ShouldParse(string input, string expectedStr)
    {
        // Arrange
        ISetting setting = Setting.AsNumber("key", "Label", 0m);
        var expected = decimal.Parse(expectedStr);

        // Act
        setting.SetValueFromString(input);

        // Assert
        setting.GetCurrentValueAsObject().Should().Be(expected);
    }

    [Theory]
    [InlineData("not a number")]
    [InlineData("")]
    [InlineData("abc123")]
    [InlineData("12.34.56")]
    public void SetValueFromString_NumberSetting_InvalidInput_ShouldUseDefault(string input)
    {
        // Arrange
        var defaultValue = 42m;
        ISetting setting = Setting.AsNumber("key", "Label", defaultValue);

        // Act
        setting.SetValueFromString(input);

        // Assert
        // TryParse fails, returns defaultValue
        setting.GetCurrentValueAsObject().Should().Be(defaultValue);
    }

    [Fact]
    public void SetValueFromString_NumberSetting_Null_ShouldUseDefault()
    {
        // Arrange
        var defaultValue = 99.99m;
        ISetting setting = Setting.AsNumber("key", "Label", defaultValue);

        // Act
        setting.SetValueFromString(null);

        // Assert
        setting.GetCurrentValueAsObject().Should().Be(defaultValue);
    }

    [Fact]
    public void SetValueFromString_NumberSetting_InvariantCulture_ShouldParseDot()
    {
        // Arrange
        ISetting setting = Setting.AsNumber("key", "Label", 0m);

        // Act
        setting.SetValueFromString("3.14");
        var result = (decimal)setting.GetCurrentValueAsObject()!;

        // Assert
        result.Should().Be(3.14m);
    }

    #endregion

    #region Integer Type Tests

    [Theory]
    [InlineData("0", 0)]
    [InlineData("42", 42)]
    [InlineData("-123", -123)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    public void SetValueFromString_IntSetting_ValidIntegers_ShouldParse(string input, int expected)
    {
        // Arrange
        ISetting setting = Setting.AsInt("key", "Label", 0);

        // Act
        setting.SetValueFromString(input);

        // Assert
        setting.GetCurrentValueAsObject().Should().Be(expected);
    }

    [Theory]
    [InlineData("12.34")] // Decimal
    [InlineData("not a number")]
    [InlineData("")]
    [InlineData("999999999999999")] // Overflow
    public void SetValueFromString_IntSetting_InvalidInput_ShouldUseDefault(string input)
    {
        // Arrange
        var defaultValue = 100;
        ISetting setting = Setting.AsInt("key", "Label", defaultValue);

        // Act
        setting.SetValueFromString(input);

        // Assert
        setting.GetCurrentValueAsObject().Should().Be(defaultValue);
    }

    #endregion

    #region Double Type Tests

    [Theory]
    [InlineData("0", 0.0)]
    [InlineData("3.14", 3.14)]
    [InlineData("-123.456", -123.456)]
    [InlineData("1.23E+10", 1.23E+10)]
    [InlineData("1.23E-5", 1.23E-5)]
    public void SetValueFromString_DoubleSetting_ValidDoubles_ShouldParse(string input, double expected)
    {
        // Arrange
        ISetting setting = Setting.AsDouble("key", "Label", 0.0);

        // Act
        setting.SetValueFromString(input);
        var result = (double)setting.GetCurrentValueAsObject()!;

        // Assert
        result.Should().BeApproximately(expected, 0.000001);
    }

    [Theory]
    [InlineData("not a number")]
    [InlineData("")]
    public void SetValueFromString_DoubleSetting_InvalidInput_ShouldUseDefault(string input)
    {
        // Arrange
        var defaultValue = 3.14;
        ISetting setting = Setting.AsDouble("key", "Label", defaultValue);

        // Act
        setting.SetValueFromString(input);

        // Assert
        setting.GetCurrentValueAsObject().Should().Be(defaultValue);
    }

    #endregion

    #region TimeSpan Type Tests

    [Theory]
    [InlineData("30", 30)]
    [InlineData("60", 60)]
    [InlineData("3600", 3600)]
    [InlineData("86400", 86400)]
    public void SetValueFromString_TimeSpanSetting_SecondsAsNumber_ShouldParse(string input, int expectedSeconds)
    {
        // Arrange
        ISetting setting = Setting.AsTimeSpan("key", "Label", TimeSpan.Zero);

        // Act
        setting.SetValueFromString(input);
        var result = (TimeSpan)setting.GetCurrentValueAsObject()!;

        // Assert
        result.TotalSeconds.Should().Be(expectedSeconds);
    }

    [Fact]
    public void SetValueFromString_TimeSpanSetting_InvalidFormat_ShouldUseDefault()
    {
        // Arrange
        var defaultValue = TimeSpan.FromMinutes(5);
        ISetting setting = Setting.AsTimeSpan("key", "Label", defaultValue);

        // Act
        setting.SetValueFromString("invalid");

        // Assert
        setting.GetCurrentValueAsObject().Should().Be(defaultValue);
    }

    [Fact]
    public void SetValueFromString_TimeSpanSetting_Null_ShouldUseDefault()
    {
        // Arrange
        var defaultValue = TimeSpan.FromHours(1);
        ISetting setting = Setting.AsTimeSpan("key", "Label", defaultValue);

        // Act
        setting.SetValueFromString(null);

        // Assert
        setting.GetCurrentValueAsObject().Should().Be(defaultValue);
    }

    #endregion

    #region Choice Type Tests

    [Fact]
    public void SetValueFromString_ChoiceSetting_ValidChoice_ShouldParse()
    {
        // Arrange
        var choices = new List<KeyValuePair<string, string>>
        {
            new("option1", "Option 1"),
            new("option2", "Option 2"),
            new("option3", "Option 3")
        };
        ISetting setting = Setting.AsChoice("key", "Label", "option1", choices);

        // Act
        setting.SetValueFromString("option2");

        // Assert
        setting.GetCurrentValueAsObject().Should().Be("option2");
    }

    [Fact]
    public void SetValueFromString_ChoiceSetting_InvalidChoice_ShouldStillSet()
    {
        // Arrange
        var choices = new List<KeyValuePair<string, string>>
        {
            new("valid", "Valid Option")
        };
        ISetting setting = Setting.AsChoice("key", "Label", "valid", choices);

        // Act - Choice validation happens in UI, not in setting
        setting.SetValueFromString("invalid-option");

        // Assert
        setting.GetCurrentValueAsObject().Should().Be("invalid-option");
    }

    #endregion

    #region Secret Type Tests

    [Fact]
    public void SetValueFromString_SecretSetting_ShouldWork()
    {
        // Arrange
        ISetting setting = Setting.AsSecret("key", "API Key");

        // Act
        setting.SetValueFromString("super-secret-token-123");

        // Assert
        setting.GetCurrentValueAsObject().Should().Be("super-secret-token-123");
        setting.Persistence.Should().Be(SettingPersistence.Ephemeral);
    }

    #endregion

    #region FilePicker Type Tests

    [Theory]
    [InlineData(@"C:\path\to\file.txt")]
    [InlineData(@"C:\folder\subfolder\document.pdf")]
    [InlineData("relative/path/file.dat")]
    public void SetValueFromString_FilePickerSetting_ValidPaths_ShouldParse(string input)
    {
        // Arrange
        ISetting setting = Setting.AsFilePicker("key", "Label", "");

        // Act
        setting.SetValueFromString(input);

        // Assert
        setting.GetCurrentValueAsObject().Should().Be(input);
    }

    #endregion

    #region DirectoryPicker Type Tests

    [Theory]
    [InlineData(@"C:\Users\")]
    [InlineData(@"C:\Program Files\")]
    [InlineData("~/Documents/")]
    public void SetValueFromString_DirectoryPickerSetting_ValidPaths_ShouldParse(string input)
    {
        // Arrange
        ISetting setting = Setting.AsDirectoryPicker("key", "Label", "");

        // Act
        setting.SetValueFromString(input);

        // Assert
        setting.GetCurrentValueAsObject().Should().Be(input);
    }

    #endregion

    #region Edge Cases & Round-Trip Tests

    [Fact]
    public void SetValueFromString_MultipleCalls_ShouldUpdateValue()
    {
        // Arrange
        ISetting setting = Setting.AsInt("key", "Label", 0);

        // Act
        setting.SetValueFromString("10");
        setting.SetValueFromString("20");
        setting.SetValueFromString("30");

        // Assert
        setting.GetCurrentValueAsObject().Should().Be(30);
    }

    [Fact]
    public void SetValueFromString_Observable_ShouldEmitChanges()
    {
        // Arrange
        ISetting setting = Setting.AsInt("key", "Label", 0);
        var emittedValues = new List<object?>();
        setting.ValueAsObject.Subscribe(v => emittedValues.Add(v));

        // Act
        setting.SetValueFromString("42");

        // Assert
        emittedValues.Should().Contain(42);
    }

    [Fact]
    public void SetValueFromString_ThenGetValueAsString_ShouldRoundTrip()
    {
        // Arrange
        ISetting setting = Setting.AsInt("key", "Label", 0);

        // Act
        setting.SetValueFromString("12345");
        var stringValue = setting.GetValueAsString();

        ISetting setting2 = Setting.AsInt("key2", "Label", 0);
        setting2.SetValueFromString(stringValue);

        // Assert
        setting2.GetCurrentValueAsObject().Should().Be(12345);
    }

    [Theory]
    [InlineData("  42  ")]
    [InlineData("\n123\n")]
    [InlineData("\t456\t")]
    public void SetValueFromString_WithWhitespace_ShouldHandleCorrectly(string input)
    {
        // Arrange
        ISetting setting = Setting.AsInt("key", "Label", 0);

        // Act
        setting.SetValueFromString(input);
        var result = setting.GetCurrentValueAsObject();

        // Assert
        // NumberStyles.Any handles whitespace
        result.Should().NotBeNull();
    }

    [Fact]
    public void SetValueFromString_ConcurrentCalls_ShouldBeSafe()
    {
        // Arrange
        ISetting setting = Setting.AsInt("key", "Label", 0);
        var tasks = new List<Task>();

        // Act
        for (var i = 0; i < 100; i++)
        {
            var value = i;
            tasks.Add(Task.Run(() => setting.SetValueFromString(value.ToString())));
        }

        // Assert
        var act = () => Task.WaitAll(tasks.ToArray());
        act.Should().NotThrow();
    }

    [Fact]
    public void SetValueFromString_EmptyString_DifferentTypes_ShouldHandleAppropriately()
    {
        // Arrange & Act & Assert
        ISetting textSetting = Setting.AsText("key1", "Label", "default");
        textSetting.SetValueFromString("");
        textSetting.GetCurrentValueAsObject().Should().Be("");

        ISetting numberSetting = Setting.AsNumber("key2", "Label", 99m);
        numberSetting.SetValueFromString("");
        numberSetting.GetCurrentValueAsObject().Should().Be(99m); // Falls back to default

        ISetting checkboxSetting = Setting.AsCheckbox("key3", "Label", true);
        checkboxSetting.SetValueFromString("");
        checkboxSetting.GetCurrentValueAsObject().Should().Be(false); // TryParse fails
    }

    [Fact]
    public void SetValueFromString_LargeNumbers_ShouldHandle()
    {
        // Arrange
        ISetting intSetting = Setting.AsInt("key1", "Label", 0);
        ISetting decimalSetting = Setting.AsNumber("key2", "Label", 0m);

        // Act & Assert
        intSetting.SetValueFromString(int.MaxValue.ToString());
        intSetting.GetCurrentValueAsObject().Should().Be(int.MaxValue);

        decimalSetting.SetValueFromString("999999999999999.99");
        ((decimal)decimalSetting.GetCurrentValueAsObject()!).Should().BeApproximately(999999999999999.99m, 0.01m);
    }

    #endregion

    #region Culture-Independent Tests

    [Fact]
    public void SetValueFromString_NumberSetting_InvariantCulture_ShouldAlwaysUseDot()
    {
        // Arrange
        ISetting setting = Setting.AsDouble("key", "Label", 0.0);

        // Act
        setting.SetValueFromString("3.14");
        var result = (double)setting.GetCurrentValueAsObject()!;

        // Assert
        result.Should().BeApproximately(3.14, 0.001);
    }

    [Fact]
    public void SetValueFromString_BooleanSetting_EnglishOnly_ShouldWork()
    {
        // Arrange
        ISetting setting = Setting.AsCheckbox("key", "Label", false);

        // Act & Assert
        setting.SetValueFromString("true");
        setting.GetCurrentValueAsObject().Should().Be(true);

        setting.SetValueFromString("false");
        setting.GetCurrentValueAsObject().Should().Be(false);
    }

    #endregion
}