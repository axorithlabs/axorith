using System.Globalization;

namespace Axorith.Contracts;

internal static class SettingValueCodec
{
    internal static SettingValue Create(string key, object? value)
    {
        var message = new SettingValue { Key = key };
        Set(message, value);
        return message;
    }

    internal static void Set(UpdateSettingRequest message, object? value)
    {
        switch (value)
        {
            case bool boolean:
                message.BoolValue = boolean;
                break;
            case int integer:
                message.IntValue = integer;
                break;
            case double number:
                message.NumberValue = number;
                break;
            case decimal number:
                message.NumberValue = (double)number;
                break;
            default:
                message.StringValue = value?.ToString() ?? string.Empty;
                break;
        }
    }

    internal static object? Get(UpdateSettingRequest message) => message.ValueCase switch
    {
        UpdateSettingRequest.ValueOneofCase.StringValue => message.StringValue,
        UpdateSettingRequest.ValueOneofCase.BoolValue => message.BoolValue,
        UpdateSettingRequest.ValueOneofCase.NumberValue => message.NumberValue,
        UpdateSettingRequest.ValueOneofCase.IntValue => message.IntValue,
        _ => null
    };

    internal static string? GetString(SettingValue message) => message.ValueCase switch
    {
        SettingValue.ValueOneofCase.StringValue => message.StringValue,
        SettingValue.ValueOneofCase.BoolValue => message.BoolValue.ToString(),
        SettingValue.ValueOneofCase.NumberValue => message.NumberValue.ToString(CultureInfo.InvariantCulture),
        SettingValue.ValueOneofCase.IntValue => message.IntValue.ToString(CultureInfo.InvariantCulture),
        _ => null
    };

    internal static string GetString(Setting message) => message.ValueCase switch
    {
        Setting.ValueOneofCase.StringValue => message.StringValue,
        Setting.ValueOneofCase.BoolValue => message.BoolValue.ToString(),
        Setting.ValueOneofCase.NumberValue => message.NumberValue.ToString(CultureInfo.InvariantCulture),
        Setting.ValueOneofCase.IntValue => message.IntValue.ToString(CultureInfo.InvariantCulture),
        Setting.ValueOneofCase.DecimalString => message.DecimalString,
        _ => string.Empty
    };

    internal static object? Get(SettingUpdate message) => message.ValueCase switch
    {
        SettingUpdate.ValueOneofCase.StringValue => message.StringValue,
        SettingUpdate.ValueOneofCase.BoolValue => message.BoolValue,
        SettingUpdate.ValueOneofCase.NumberValue => message.NumberValue,
        SettingUpdate.ValueOneofCase.IntValue => message.IntValue,
        SettingUpdate.ValueOneofCase.DecimalString => message.DecimalString,
        SettingUpdate.ValueOneofCase.ChoiceList => message.ChoiceList?.Choices
            .Select(c => new KeyValuePair<string, string>(c.Key, c.Display))
            .ToList(),
        _ => null
    };

    private static void Set(SettingValue message, object? value)
    {
        switch (value)
        {
            case bool boolean:
                message.BoolValue = boolean;
                break;
            case int integer:
                message.IntValue = integer;
                break;
            case double number:
                message.NumberValue = number;
                break;
            case decimal number:
                message.NumberValue = (double)number;
                break;
            default:
                message.StringValue = value?.ToString() ?? string.Empty;
                break;
        }
    }
}
