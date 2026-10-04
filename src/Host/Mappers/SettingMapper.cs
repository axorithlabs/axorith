using System.Globalization;
using Axorith.Contracts;
using Axorith.Sdk.Settings;
using Setting = Axorith.Contracts.Setting;
using SettingControlType = Axorith.Contracts.SettingControlType;
using SettingPersistence = Axorith.Contracts.SettingPersistence;

namespace Axorith.Host.Mappers;

public static class SettingMapper
{
    public static Setting ToMessage(ISetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);

        var message = new Setting
        {
            Key = setting.Key,
            Label = setting.GetCurrentLabel(),
            Description = setting.Description ?? string.Empty,
            ControlType = ToMessageControlType(setting.ControlType),
            Persistence = (SettingPersistence)setting.Persistence,
            IsVisible = setting.GetCurrentVisibility(),
            IsReadOnly = setting.GetCurrentReadOnly(),
            ValueType = setting.ValueType == typeof(List<string>) ? "List<String>" :
                setting.ValueType == typeof(string) || setting.ValueType == typeof(bool) ||
                setting.ValueType == typeof(int) || setting.ValueType == typeof(decimal) ||
                setting.ValueType == typeof(double) || setting.ValueType == typeof(TimeSpan)
                    ? setting.ValueType.Name
                    : "String",
            Filter = setting.ControlType == Sdk.Settings.SettingControlType.FilePicker
                ? setting.Filter ?? string.Empty
                : string.Empty,
            HasHistory = setting.HasHistory
        };

        var currentValue = setting.GetCurrentValueAsObject();
        switch (setting.ControlType)
        {
            case Sdk.Settings.SettingControlType.Text:
            case Sdk.Settings.SettingControlType.TextArea:
            case Sdk.Settings.SettingControlType.Secret:
            case Sdk.Settings.SettingControlType.FilePicker:
            case Sdk.Settings.SettingControlType.DirectoryPicker:
            case Sdk.Settings.SettingControlType.Choice:
            case Sdk.Settings.SettingControlType.MultiChoice:
            case Sdk.Settings.SettingControlType.Button:
                message.StringValue = currentValue?.ToString() ?? string.Empty;
                break;

            case Sdk.Settings.SettingControlType.Checkbox:
                message.BoolValue = currentValue is true;
                break;

            case Sdk.Settings.SettingControlType.Number:
                switch (currentValue)
                {
                    case decimal dec:
                        message.DecimalString = dec.ToString(CultureInfo.InvariantCulture);
                        break;
                    case double d:
                        message.NumberValue = d;
                        break;
                    case int i:
                        message.IntValue = i;
                        break;
                }

                break;
        }

        if (setting.GetCurrentChoices() is { } choices)
            message.Choices.AddRange(choices.Select(choice => new Choice { Key = choice.Key, Display = choice.Value }));

        return message;
    }

    public static SettingUpdate CreateUpdate(Guid moduleInstanceId, string settingKey,
        SettingProperty property, object? value)
    {
        var update = new SettingUpdate
        {
            ModuleInstanceId = moduleInstanceId.ToString(),
            SettingKey = settingKey,
            Property = (SettingProperty)(int)property
        };

        switch (property)
        {
            case SettingProperty.Value:
                SetUpdateValue(update, value);
                break;
            case SettingProperty.Label:
            case SettingProperty.ActionLabel:
                update.StringValue = value?.ToString() ?? string.Empty;
                break;
            case SettingProperty.Visibility:
            case SettingProperty.ReadOnly:
            case SettingProperty.ActionEnabled:
                update.BoolValue = value is true;
                break;
            case SettingProperty.Choices:
                if (value is IReadOnlyList<KeyValuePair<string, string>> choices)
                {
                    var choiceList = new ChoiceList();
                    foreach (var (key, display) in choices)
                    {
                        choiceList.Choices.Add(new Choice { Key = key, Display = display });
                    }

                    update.ChoiceList = choiceList;
                }

                break;
        }

        return update;
    }

    private static void SetUpdateValue(SettingUpdate update, object? value)
    {
        switch (value)
        {
            case string s:
                update.StringValue = s;
                break;
            case bool b:
                update.BoolValue = b;
                break;
            case double d:
                update.NumberValue = d;
                break;
            case int i:
                update.IntValue = i;
                break;
            case decimal dec:
                update.DecimalString = dec.ToString(CultureInfo.InvariantCulture);
                break;
            case TimeSpan ts:
                update.StringValue = ts.ToString();
                break;
            case List<string> list:
                update.StringValue = string.Join("|", list);
                break;
            default:
                update.StringValue = value?.ToString() ?? string.Empty;
                break;
        }
    }

    private static SettingControlType ToMessageControlType(Sdk.Settings.SettingControlType controlType)
    {
        return controlType switch
        {
            Sdk.Settings.SettingControlType.Text => SettingControlType.Text,
            Sdk.Settings.SettingControlType.TextArea => SettingControlType.TextArea,
            Sdk.Settings.SettingControlType.Checkbox => SettingControlType.Checkbox,
            Sdk.Settings.SettingControlType.Number => SettingControlType.Number,
            Sdk.Settings.SettingControlType.Choice => SettingControlType.Choice,
            Sdk.Settings.SettingControlType.MultiChoice => SettingControlType.MultiChoice,
            Sdk.Settings.SettingControlType.Secret => SettingControlType.Secret,
            Sdk.Settings.SettingControlType.FilePicker => SettingControlType.FilePicker,
            Sdk.Settings.SettingControlType.DirectoryPicker => SettingControlType.DirectoryPicker,
            Sdk.Settings.SettingControlType.Button => SettingControlType.Button,
            _ => SettingControlType.Text
        };
    }
}
