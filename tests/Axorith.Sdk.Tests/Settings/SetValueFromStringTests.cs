using System.Globalization;
using Axorith.Sdk.Settings;
using FluentAssertions;

namespace Axorith.Sdk.Tests.Settings;

public class SetValueFromStringTests
{
    [Theory]
    [InlineData("simple text")]
    [InlineData("")]
    [InlineData("with\nnewlines\nand\ttabs")]
    [InlineData("unicode: 你好世界 🚀")]
    [InlineData("special !@#$%^&*()_+-=[]{}")]
    public void TextSetting_Parses(string input) =>
        SetAndAssert(Setting.AsText("key", "Label", "default"), input, input, assertRoundTrip: true);

    [Fact]
    public void TextSetting_NullUsesDefault() =>
        SetAndAssert(Setting.AsText("key", "Label", "default-text"), null, "default-text");

    [Fact]
    public void TextAreaSetting_ParsesMultiline()
    {
        const string input = "Line 1\nLine 2\nLine 3";
        SetAndAssert(Setting.AsTextArea("key", "Label", "default"), input, input);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("FALSE", false)]
    public void CheckboxSetting_ParsesBooleans(string input, bool expected) =>
        SetAndAssert(Setting.AsCheckbox("key", "Label", false), input, expected, assertRoundTrip: true);

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("invalid")]
    [InlineData("")]
    [InlineData(null)]
    public void CheckboxSetting_InvalidInputUsesFalse(string? input) =>
        SetAndAssert(Setting.AsCheckbox("key", "Label", true), input, false);

    [Theory]
    [InlineData("0", "0")]
    [InlineData("123.45", "123.45")]
    [InlineData("-999.99", "-999.99")]
    [InlineData("0.001", "0.001")]
    [InlineData("1234567890.123456789", "1234567890.123456789")]
    public void NumberSetting_ParsesInvariantDecimals(string input, string expected) =>
        SetAndAssert(Setting.AsNumber("key", "Label", 0m), input,
            decimal.Parse(expected, CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("not a number")]
    [InlineData("")]
    [InlineData("abc123")]
    [InlineData("12.34.56")]
    [InlineData(null)]
    public void NumberSetting_InvalidInputUsesDefault(string? input) =>
        SetAndAssert(Setting.AsNumber("key", "Label", 42m), input, 42m);

    [Theory]
    [InlineData("0", 0)]
    [InlineData("42", 42)]
    [InlineData("-123", -123)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    public void IntSetting_Parses(string input, int expected) =>
        SetAndAssert(Setting.AsInt("key", "Label", 0), input, expected);

    [Theory]
    [InlineData("12.34")]
    [InlineData("not a number")]
    [InlineData("")]
    [InlineData("999999999999999")]
    public void IntSetting_InvalidInputUsesDefault(string input) =>
        SetAndAssert(Setting.AsInt("key", "Label", 100), input, 100);

    [Theory]
    [InlineData("0", 0.0)]
    [InlineData("3.14", 3.14)]
    [InlineData("-123.456", -123.456)]
    [InlineData("1.23E+10", 1.23E+10)]
    [InlineData("1.23E-5", 1.23E-5)]
    public void DoubleSetting_ParsesInvariantValues(string input, double expected)
    {
        ISetting setting = Setting.AsDouble("key", "Label", 0);
        setting.SetValueFromString(input);
        ((double)setting.GetCurrentValueAsObject()!).Should().BeApproximately(expected, 0.000001);
    }

    [Theory]
    [InlineData("not a number")]
    [InlineData("")]
    public void DoubleSetting_InvalidInputUsesDefault(string input) =>
        SetAndAssert(Setting.AsDouble("key", "Label", 3.14), input, 3.14);

    [Theory]
    [InlineData("30", 30)]
    [InlineData("60", 60)]
    [InlineData("3600", 3600)]
    [InlineData("86400", 86400)]
    public void TimeSpanSetting_ParsesSeconds(string input, int expectedSeconds)
    {
        ISetting setting = Setting.AsTimeSpan("key", "Label", TimeSpan.Zero);
        setting.SetValueFromString(input);
        ((TimeSpan)setting.GetCurrentValueAsObject()!).TotalSeconds.Should().Be(expectedSeconds);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData(null)]
    public void TimeSpanSetting_InvalidInputUsesDefault(string? input)
    {
        var expected = TimeSpan.FromMinutes(5);
        SetAndAssert(Setting.AsTimeSpan("key", "Label", expected), input, expected);
    }

    [Theory]
    [InlineData("option2")]
    [InlineData("invalid-option")]
    public void ChoiceSetting_SetsProvidedKey(string input)
    {
        var choices = new[] { KeyValuePair.Create("option1", "Option 1"), KeyValuePair.Create("option2", "Option 2") };
        SetAndAssert(Setting.AsChoice("key", "Label", "option1", choices), input, input);
    }

    [Fact]
    public void SecretSetting_ParsesAndIsEphemeral()
    {
        ISetting setting = Setting.AsSecret("key", "API Key");
        setting.SetValueFromString("super-secret-token-123");
        setting.GetCurrentValueAsObject().Should().Be("super-secret-token-123");
        setting.Persistence.Should().Be(SettingPersistence.Ephemeral);
    }

    [Theory]
    [InlineData(@"C:\path\to\file.txt")]
    [InlineData(@"C:\folder\subfolder\document.pdf")]
    [InlineData("relative/path/file.dat")]
    public void FilePickerSetting_Parses(string input) =>
        SetAndAssert(Setting.AsFilePicker("key", "Label", ""), input, input);

    [Theory]
    [InlineData(@"C:\Users\")]
    [InlineData(@"C:\Program Files\")]
    [InlineData("~/Documents/")]
    public void DirectoryPickerSetting_Parses(string input) =>
        SetAndAssert(Setting.AsDirectoryPicker("key", "Label", ""), input, input);

    [Fact]
    public void MultipleCalls_UpdateValue()
    {
        ISetting setting = Setting.AsInt("key", "Label", 0);
        setting.SetValueFromString("10");
        setting.SetValueFromString("20");
        setting.SetValueFromString("30");
        setting.GetCurrentValueAsObject().Should().Be(30);
    }

    [Fact]
    public void Observable_EmitsChanges()
    {
        ISetting setting = Setting.AsInt("key", "Label", 0);
        var values = new List<object?>();
        setting.ValueAsObject.Subscribe(values.Add);
        setting.SetValueFromString("42");
        values.Should().Contain(42);
    }

    [Fact]
    public void IntSetting_RoundTripsThroughString()
    {
        ISetting source = Setting.AsInt("source", "Label", 0);
        ISetting target = Setting.AsInt("target", "Label", 0);
        source.SetValueFromString("12345");
        target.SetValueFromString(source.GetValueAsString());
        target.GetCurrentValueAsObject().Should().Be(12345);
    }

    [Theory]
    [InlineData("  42  ", 42)]
    [InlineData("\n123\n", 123)]
    [InlineData("\t456\t", 456)]
    public void IntSetting_AllowsWhitespace(string input, int expected)
    {
        ISetting setting = Setting.AsInt("key", "Label", 0);
        setting.SetValueFromString(input);
        setting.GetCurrentValueAsObject().Should().Be(expected);
    }

    [Fact]
    public void ConcurrentCalls_DoNotThrow()
    {
        ISetting setting = Setting.AsInt("key", "Label", 0);
        var tasks = Enumerable.Range(0, 100)
            .Select(value => Task.Run(() => setting.SetValueFromString(value.ToString())))
            .ToArray();
        Action wait = () => Task.WaitAll(tasks);
        wait.Should().NotThrow();
    }

    [Fact]
    public void EmptyString_UsesTypeSpecificBehavior()
    {
        SetAndAssert(Setting.AsText("text", "Label", "default"), "", "");
        SetAndAssert(Setting.AsNumber("number", "Label", 99m), "", 99m);
        SetAndAssert(Setting.AsCheckbox("checkbox", "Label", true), "", false);
    }

    [Fact]
    public void LargeNumbers_ParseWithinTypeRange()
    {
        SetAndAssert(Setting.AsInt("int", "Label", 0), int.MaxValue.ToString(), int.MaxValue);
        ISetting number = Setting.AsNumber("decimal", "Label", 0m);
        number.SetValueFromString("999999999999999.99");
        ((decimal)number.GetCurrentValueAsObject()!).Should().BeApproximately(999999999999999.99m, 0.01m);
    }

    private static void SetAndAssert(ISetting setting, string? input, object? expected, bool assertRoundTrip = false)
    {
        setting.SetValueFromString(input);
        setting.GetCurrentValueAsObject().Should().Be(expected);
        if (assertRoundTrip) setting.GetValueAsString().Should().Be(expected?.ToString());
    }
}
