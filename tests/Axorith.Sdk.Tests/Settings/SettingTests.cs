using Axorith.Sdk.Settings;
using FluentAssertions;

namespace Axorith.Sdk.Tests.Settings;

public class SettingTests
{

    [Fact]
    public void AsText_ShouldCreateTextSetting()
    {
        var setting = Setting.AsText("key", "Label", "default");

        setting.Should().NotBeNull();
        setting.Key.Should().Be("key");
        setting.ControlType.Should().Be(SettingControlType.Text);
        setting.Persistence.Should().Be(SettingPersistence.Persisted);
        setting.GetCurrentValue().Should().Be("default");
    }

    [Fact]
    public void AsText_SetValue_ShouldUpdateValue()
    {
        var setting = Setting.AsText("key", "Label", "initial");
        var emittedValues = new List<string>();
        setting.Value.Subscribe(v => emittedValues.Add(v));

        setting.SetValue("updated");

        emittedValues.Should().Equal("initial", "updated");
        setting.GetCurrentValue().Should().Be("updated");
    }

    [Fact]
    public void AsTextArea_ShouldCreateTextAreaSetting()
    {
        var setting = Setting.AsTextArea("sites", "Blocked Sites", "youtube.com,twitter.com");

        setting.ControlType.Should().Be(SettingControlType.TextArea);
        setting.GetCurrentValue().Should().Be("youtube.com,twitter.com");
    }



    [Fact]
    public void AsCheckbox_ShouldCreateBooleanSetting()
    {
        var setting = Setting.AsCheckbox("enabled", "Enable Feature", defaultValue: true);

        setting.ControlType.Should().Be(SettingControlType.Checkbox);
        setting.GetCurrentValue().Should().BeTrue();
    }

    [Fact]
    public void AsCheckbox_SetValue_ShouldToggleBoolean()
    {
        var setting = Setting.AsCheckbox("toggle", "Toggle", false);
        var values = new List<bool>();
        setting.Value.Subscribe(v => values.Add(v));

        setting.SetValue(true);
        setting.SetValue(false);

        values.Should().Equal(false, true, false);
    }



    [Fact]
    public void AsNumber_ShouldCreateDecimalSetting()
    {
        var setting = Setting.AsNumber("duration", "Duration", 10.5m);

        setting.ControlType.Should().Be(SettingControlType.Number);
        setting.GetCurrentValue().Should().Be(10.5m);
    }

    [Fact]
    public void AsInt_ShouldCreateIntegerSetting()
    {
        var setting = Setting.AsInt("count", "Count", 42);

        setting.ControlType.Should().Be(SettingControlType.Number);
        setting.ValueType.Should().Be(typeof(int));
        setting.GetCurrentValue().Should().Be(42);
    }

    [Fact]
    public void AsDouble_ShouldCreateDoubleSetting()
    {
        var setting = Setting.AsDouble("percentage", "Percentage", 99.9);

        setting.ValueType.Should().Be(typeof(double));
        setting.GetCurrentValue().Should().Be(99.9);
    }

    [Fact]
    public void AsTimeSpan_ShouldCreateTimeSpanSetting()
    {
        var duration = TimeSpan.FromMinutes(5);

        var setting = Setting.AsTimeSpan("timeout", "Timeout", duration);

        setting.ValueType.Should().Be(typeof(TimeSpan));
        setting.GetCurrentValue().Should().Be(duration);
    }



    [Fact]
    public void AsChoice_ShouldCreateChoiceSetting()
    {
        var choices = new List<KeyValuePair<string, string>>
        {
            new("option1", "Option 1"),
            new("option2", "Option 2")
        };

        var setting = Setting.AsChoice("mode", "Mode", "option1", choices);

        setting.ControlType.Should().Be(SettingControlType.Choice);
        setting.GetCurrentValue().Should().Be("option1");
        setting.Choices.Should().NotBeNull();
    }

    [Fact]
    public void SetChoices_ShouldUpdateAvailableChoices()
    {
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

        setting.SetChoices(newChoices);

        emittedChoices.Should().HaveCount(2);
        emittedChoices[1].Should().HaveCount(2);
        emittedChoices[1].Should().Contain(new KeyValuePair<string, string>("b", "B"));
    }

    [Fact]
    public void SetChoices_WithEmptyList_ShouldNotThrow()
    {
        var choices = new List<KeyValuePair<string, string>>
        {
            new("opt", "Option")
        };
        var setting = Setting.AsChoice("choice", "Choice", "opt", choices);

        var act = () => setting.SetChoices([]);

        act.Should().NotThrow();
    }

    [Fact]
    public void SetChoices_WithNull_ShouldThrow()
    {
        var choices = new List<KeyValuePair<string, string>> { new("k", "V") };
        var setting = Setting.AsChoice("choice", "Choice", "k", choices);

        var act = () => setting.SetChoices(null!);

        act.Should().Throw<ArgumentNullException>();
    }



    [Fact]
    public void AsSecret_ShouldCreateEphemeralSetting()
    {
        var setting = Setting.AsSecret("token", "API Token");

        setting.ControlType.Should().Be(SettingControlType.Secret);
        setting.Persistence.Should().Be(SettingPersistence.Ephemeral);
        setting.GetCurrentValue().Should().BeEmpty();
    }



    [Fact]
    public void AsFilePicker_ShouldCreateFilePickerSetting()
    {
        var setting = Setting.AsFilePicker("file", "Config File", "/path/to/file.json", "*.json");

        setting.ControlType.Should().Be(SettingControlType.FilePicker);
        setting.Filter.Should().Be("*.json");
        setting.GetCurrentValue().Should().Be("/path/to/file.json");
    }

    [Fact]
    public void AsDirectoryPicker_ShouldCreateDirectoryPickerSetting()
    {
        var setting = Setting.AsDirectoryPicker("dir", "Output Directory", "/output");

        setting.ControlType.Should().Be(SettingControlType.DirectoryPicker);
        setting.GetCurrentValue().Should().Be("/output");
    }



    [Fact]
    public void SetLabel_ShouldUpdateLabelObservable()
    {
        var setting = Setting.AsText("key", "Old Label", "value");
        var labels = new List<string>();
        setting.Label.Subscribe(l => labels.Add(l));

        setting.SetLabel("New Label");

        labels.Should().Equal("Old Label", "New Label");
    }

    [Fact]
    public void SetLabel_WithEmptyString_ShouldThrow()
    {
        var setting = Setting.AsText("key", "Label", "value");

        var act = () => setting.SetLabel("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetVisibility_ShouldUpdateIsVisibleObservable()
    {
        var setting = Setting.AsText("key", "Label", "value", isVisible: true);
        var visibilities = new List<bool>();
        setting.IsVisible.Subscribe(v => visibilities.Add(v));

        setting.SetVisibility(false);
        setting.SetVisibility(true);

        visibilities.Should().Equal(true, false, true);
    }

    [Fact]
    public void SetReadOnly_ShouldUpdateIsReadOnlyObservable()
    {
        var setting = Setting.AsText("key", "Label", "value", isReadOnly: false);
        var readOnlyStates = new List<bool>();
        setting.IsReadOnly.Subscribe(r => readOnlyStates.Add(r));

        setting.SetReadOnly(true);

        readOnlyStates.Should().Equal(false, true);
    }



    [Fact]
    public void ISetting_GetValueAsString_ShouldSerializeValue()
    {
        var setting = Setting.AsInt("count", "Count", 123);
        var iSetting = (ISetting)setting;

        var stringValue = iSetting.GetValueAsString();

        stringValue.Should().Be("123");
    }

    [Fact]
    public void ISetting_SetValueFromString_ShouldDeserializeValue()
    {
        var setting = Setting.AsInt("count", "Count", 0);
        var iSetting = (ISetting)setting;

        iSetting.SetValueFromString("456");

        setting.GetCurrentValue().Should().Be(456);
    }

    [Fact]
    public void ISetting_GetCurrentValueAsObject_ShouldReturnBoxedValue()
    {
        var setting = Setting.AsCheckbox("enabled", "Enabled", true);
        var iSetting = (ISetting)setting;

        var objectValue = iSetting.GetCurrentValueAsObject();

        objectValue.Should().BeOfType<bool>();
        objectValue.Should().Be(true);
    }

    [Fact]
    public void ISetting_SetValueFromObject_WithMatchingType_ShouldSetValue()
    {
        var setting = Setting.AsText("key", "Label", "old");
        var iSetting = (ISetting)setting;

        iSetting.SetValueFromObject("new");

        setting.GetCurrentValue().Should().Be("new");
    }

    [Fact]
    public void ISetting_SetValueFromObject_WithConvertibleType_ShouldConvert()
    {
        var setting = Setting.AsInt("num", "Number", 0);
        var iSetting = (ISetting)setting;

        iSetting.SetValueFromObject(42);

        setting.GetCurrentValue().Should().Be(42);
    }



    [Fact]
    public void ValueAsObject_ShouldEmitBoxedValues()
    {
        var setting = Setting.AsCheckbox("check", "Check", false);
        var emittedValues = new List<object?>();
        setting.ValueAsObject.Subscribe(v => emittedValues.Add(v));

        setting.SetValue(true);

        emittedValues.Should().HaveCount(2);
        emittedValues[0].Should().Be(false);
        emittedValues[1].Should().Be(true);
    }



    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Constructor_WithInvalidKey_ShouldThrow(string? invalidKey)
    {
        var act = () => Setting.AsText(invalidKey!, "Label", "value");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Constructor_WithInvalidLabel_ShouldThrow(string? invalidLabel)
    {
        var act = () => Setting.AsText("key", invalidLabel!, "value");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MultipleSubscribers_ShouldAllReceiveUpdates()
    {
        var setting = Setting.AsText("key", "Label", "initial");
        var subscriber1 = new List<string>();
        var subscriber2 = new List<string>();

        setting.Value.Subscribe(v => subscriber1.Add(v));
        setting.Value.Subscribe(v => subscriber2.Add(v));

        setting.SetValue("updated");

        subscriber1.Should().Equal("initial", "updated");
        subscriber2.Should().Equal("initial", "updated");
    }

}
