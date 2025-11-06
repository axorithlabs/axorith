using Axorith.Sdk.Settings;
using FluentAssertions;

namespace Axorith.Sdk.Tests.Settings;

public class SettingTests
{
    #region Text Settings

    [Fact]
    public void AsText_ShouldCreateTextSetting()
    {
        // Act
        var setting = Setting.AsText("key", "Label", "default");

        // Assert
        setting.Should().NotBeNull();
        setting.Key.Should().Be("key");
        setting.ControlType.Should().Be(SettingControlType.Text);
        setting.Persistence.Should().Be(SettingPersistence.Persisted);
        setting.GetCurrentValue().Should().Be("default");
    }

    [Fact]
    public void AsText_SetValue_ShouldUpdateValue()
    {
        // Arrange
        var setting = Setting.AsText("key", "Label", "initial");
        var emittedValues = new List<string>();
        setting.Value.Subscribe(v => emittedValues.Add(v));

        // Act
        setting.SetValue("updated");

        // Assert
        emittedValues.Should().Equal("initial", "updated");
        setting.GetCurrentValue().Should().Be("updated");
    }

    [Fact]
    public void AsTextArea_ShouldCreateTextAreaSetting()
    {
        // Act
        var setting = Setting.AsTextArea("sites", "Blocked Sites", "youtube.com,twitter.com");

        // Assert
        setting.ControlType.Should().Be(SettingControlType.TextArea);
        setting.GetCurrentValue().Should().Be("youtube.com,twitter.com");
    }

    #endregion

    #region Checkbox Settings

    [Fact]
    public void AsCheckbox_ShouldCreateBooleanSetting()
    {
        // Act
        var setting = Setting.AsCheckbox("enabled", "Enable Feature", defaultValue: true);

        // Assert
        setting.ControlType.Should().Be(SettingControlType.Checkbox);
        setting.GetCurrentValue().Should().BeTrue();
    }

    [Fact]
    public void AsCheckbox_SetValue_ShouldToggleBoolean()
    {
        // Arrange
        var setting = Setting.AsCheckbox("toggle", "Toggle", false);
        var values = new List<bool>();
        setting.Value.Subscribe(v => values.Add(v));

        // Act
        setting.SetValue(true);
        setting.SetValue(false);

        // Assert
        values.Should().Equal(false, true, false);
    }

    #endregion

    #region Number Settings

    [Fact]
    public void AsNumber_ShouldCreateDecimalSetting()
    {
        // Act
        var setting = Setting.AsNumber("duration", "Duration", 10.5m);

        // Assert
        setting.ControlType.Should().Be(SettingControlType.Number);
        setting.GetCurrentValue().Should().Be(10.5m);
    }

    [Fact]
    public void AsInt_ShouldCreateIntegerSetting()
    {
        // Act
        var setting = Setting.AsInt("count", "Count", 42);

        // Assert
        setting.ControlType.Should().Be(SettingControlType.Number);
        setting.ValueType.Should().Be(typeof(int));
        setting.GetCurrentValue().Should().Be(42);
    }

    [Fact]
    public void AsDouble_ShouldCreateDoubleSetting()
    {
        // Act
        var setting = Setting.AsDouble("percentage", "Percentage", 99.9);

        // Assert
        setting.ValueType.Should().Be(typeof(double));
        setting.GetCurrentValue().Should().Be(99.9);
    }

    [Fact]
    public void AsTimeSpan_ShouldCreateTimeSpanSetting()
    {
        // Arrange
        var duration = TimeSpan.FromMinutes(5);

        // Act
        var setting = Setting.AsTimeSpan("timeout", "Timeout", duration);

        // Assert
        setting.ValueType.Should().Be(typeof(TimeSpan));
        setting.GetCurrentValue().Should().Be(duration);
    }

    #endregion

    #region Choice Settings

    [Fact]
    public void AsChoice_ShouldCreateChoiceSetting()
    {
        // Arrange
        var choices = new List<KeyValuePair<string, string>>
        {
            new("option1", "Option 1"),
            new("option2", "Option 2")
        };

        // Act
        var setting = Setting.AsChoice("mode", "Mode", "option1", choices);

        // Assert
        setting.ControlType.Should().Be(SettingControlType.Choice);
        setting.GetCurrentValue().Should().Be("option1");
        setting.Choices.Should().NotBeNull();
    }

    [Fact]
    public void SetChoices_ShouldUpdateAvailableChoices()
    {
        // Arrange
        var initialChoices = new List<KeyValuePair<string, string>>
        {
            new("a", "A")
        };
        var setting = Setting.AsChoice("select", "Select", "a", initialChoices);

        var emittedChoices = new List<IReadOnlyList<KeyValuePair<string, string>>>();
        setting.Choices!.Subscribe(c => emittedChoices.Add(c));

        var newChoices = new List<KeyValuePair<string, string>>
        {
            new("a", "A"),
            new("b", "B")
        };

        // Act
        setting.SetChoices(newChoices);

        // Assert
        emittedChoices.Should().HaveCount(2);
        emittedChoices[1].Should().HaveCount(2);
        emittedChoices[1].Should().Contain(new KeyValuePair<string, string>("b", "B"));
    }

    [Fact]
    public void SetChoices_WithEmptyList_ShouldNotThrow()
    {
        // Arrange
        var choices = new List<KeyValuePair<string, string>>
        {
            new("opt", "Option")
        };
        var setting = Setting.AsChoice("choice", "Choice", "opt", choices);

        // Act
        var act = () => setting.SetChoices([]);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void SetChoices_WithNull_ShouldThrow()
    {
        // Arrange
        var choices = new List<KeyValuePair<string, string>> { new("k", "V") };
        var setting = Setting.AsChoice("choice", "Choice", "k", choices);

        // Act
        var act = () => setting.SetChoices(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Secret Settings

    [Fact]
    public void AsSecret_ShouldCreateEphemeralSetting()
    {
        // Act
        var setting = Setting.AsSecret("token", "API Token");

        // Assert
        setting.ControlType.Should().Be(SettingControlType.Secret);
        setting.Persistence.Should().Be(SettingPersistence.Ephemeral);
        setting.GetCurrentValue().Should().BeEmpty();
    }

    #endregion

    #region File and Directory Pickers

    [Fact]
    public void AsFilePicker_ShouldCreateFilePickerSetting()
    {
        // Act
        var setting = Setting.AsFilePicker("file", "Config File", "/path/to/file.json", "*.json");

        // Assert
        setting.ControlType.Should().Be(SettingControlType.FilePicker);
        setting.Filter.Should().Be("*.json");
        setting.GetCurrentValue().Should().Be("/path/to/file.json");
    }

    [Fact]
    public void AsDirectoryPicker_ShouldCreateDirectoryPickerSetting()
    {
        // Act
        var setting = Setting.AsDirectoryPicker("dir", "Output Directory", "/output");

        // Assert
        setting.ControlType.Should().Be(SettingControlType.DirectoryPicker);
        setting.GetCurrentValue().Should().Be("/output");
    }

    #endregion

    #region Label and Visibility

    [Fact]
    public void SetLabel_ShouldUpdateLabelObservable()
    {
        // Arrange
        var setting = Setting.AsText("key", "Old Label", "value");
        var labels = new List<string>();
        setting.Label.Subscribe(l => labels.Add(l));

        // Act
        setting.SetLabel("New Label");

        // Assert
        labels.Should().Equal("Old Label", "New Label");
    }

    [Fact]
    public void SetLabel_WithEmptyString_ShouldThrow()
    {
        // Arrange
        var setting = Setting.AsText("key", "Label", "value");

        // Act
        var act = () => setting.SetLabel("");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetVisibility_ShouldUpdateIsVisibleObservable()
    {
        // Arrange
        var setting = Setting.AsText("key", "Label", "value", isVisible: true);
        var visibilities = new List<bool>();
        setting.IsVisible.Subscribe(v => visibilities.Add(v));

        // Act
        setting.SetVisibility(false);
        setting.SetVisibility(true);

        // Assert
        visibilities.Should().Equal(true, false, true);
    }

    [Fact]
    public void SetReadOnly_ShouldUpdateIsReadOnlyObservable()
    {
        // Arrange
        var setting = Setting.AsText("key", "Label", "value", isReadOnly: false);
        var readOnlyStates = new List<bool>();
        setting.IsReadOnly.Subscribe(r => readOnlyStates.Add(r));

        // Act
        setting.SetReadOnly(true);

        // Assert
        readOnlyStates.Should().Equal(false, true);
    }

    #endregion

    #region ISetting Interface

    [Fact]
    public void ISetting_GetValueAsString_ShouldSerializeValue()
    {
        // Arrange
        var setting = Setting.AsInt("count", "Count", 123);
        var iSetting = (ISetting)setting;

        // Act
        var stringValue = iSetting.GetValueAsString();

        // Assert
        stringValue.Should().Be("123");
    }

    [Fact]
    public void ISetting_SetValueFromString_ShouldDeserializeValue()
    {
        // Arrange
        var setting = Setting.AsInt("count", "Count", 0);
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromString("456");

        // Assert
        setting.GetCurrentValue().Should().Be(456);
    }

    [Fact]
    public void ISetting_GetCurrentValueAsObject_ShouldReturnBoxedValue()
    {
        // Arrange
        var setting = Setting.AsCheckbox("enabled", "Enabled", true);
        var iSetting = (ISetting)setting;

        // Act
        var objectValue = iSetting.GetCurrentValueAsObject();

        // Assert
        objectValue.Should().BeOfType<bool>();
        objectValue.Should().Be(true);
    }

    [Fact]
    public void ISetting_SetValueFromObject_WithMatchingType_ShouldSetValue()
    {
        // Arrange
        var setting = Setting.AsText("key", "Label", "old");
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromObject("new");

        // Assert
        setting.GetCurrentValue().Should().Be("new");
    }

    [Fact]
    public void ISetting_SetValueFromObject_WithConvertibleType_ShouldConvert()
    {
        // Arrange
        var setting = Setting.AsInt("num", "Number", 0);
        var iSetting = (ISetting)setting;

        // Act
        iSetting.SetValueFromObject(42);

        // Assert
        setting.GetCurrentValue().Should().Be(42);
    }

    #endregion

    #region ValueAsObject Observable

    [Fact]
    public void ValueAsObject_ShouldEmitBoxedValues()
    {
        // Arrange
        var setting = Setting.AsCheckbox("check", "Check", false);
        var emittedValues = new List<object?>();
        setting.ValueAsObject.Subscribe(v => emittedValues.Add(v));

        // Act
        setting.SetValue(true);

        // Assert
        emittedValues.Should().HaveCount(2);
        emittedValues[0].Should().Be(false);
        emittedValues[1].Should().Be(true);
    }

    #endregion

    #region Edge Cases

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Constructor_WithInvalidKey_ShouldThrow(string? invalidKey)
    {
        // Act
        var act = () => Setting.AsText(invalidKey!, "Label", "value");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Constructor_WithInvalidLabel_ShouldThrow(string? invalidLabel)
    {
        // Act
        var act = () => Setting.AsText("key", invalidLabel!, "value");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MultipleSubscribers_ShouldAllReceiveUpdates()
    {
        // Arrange
        var setting = Setting.AsText("key", "Label", "initial");
        var subscriber1 = new List<string>();
        var subscriber2 = new List<string>();

        setting.Value.Subscribe(v => subscriber1.Add(v));
        setting.Value.Subscribe(v => subscriber2.Add(v));

        // Act
        setting.SetValue("updated");

        // Assert
        subscriber1.Should().Equal("initial", "updated");
        subscriber2.Should().Equal("initial", "updated");
    }

    #endregion
}