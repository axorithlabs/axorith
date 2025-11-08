using System.Reactive.Linq;
using System.Reactive.Subjects;
using Axorith.Client.CoreSdk;
using Axorith.Sdk.Settings;

namespace Axorith.Client.Adapters;

/// <summary>
///     Adapts a ModuleSetting from gRPC into an ISetting for UI binding.
///     Provides reactive properties for the UI without requiring a live module instance.
/// </summary>
internal class ModuleSettingAdapter : ISetting
{
    private readonly BehaviorSubject<string> _labelSubject;
    private readonly BehaviorSubject<bool> _visibilitySubject;
    private readonly BehaviorSubject<bool> _readOnlySubject;
    private readonly BehaviorSubject<object?> _valueSubject;
    private readonly BehaviorSubject<IReadOnlyList<KeyValuePair<string, string>>> _choicesSubject;

    public string Key { get; }
    public IObservable<string> Label { get; }
    public string? Description { get; }
    public SettingControlType ControlType { get; }
    public Type ValueType { get; }
    public SettingPersistence Persistence { get; }
    public IObservable<object?> ValueAsObject { get; }
    public IObservable<bool> IsVisible { get; }
    public IObservable<bool> IsReadOnly { get; }
    public IObservable<IReadOnlyList<KeyValuePair<string, string>>>? Choices { get; }
    public string? Filter { get; }

    public ModuleSettingAdapter(ModuleSetting setting, string? savedValue = null)
    {
        Key = setting.Key;
        Description = setting.Description;
        Filter = null;

        // Parse control type
        if (!Enum.TryParse<SettingControlType>(setting.ControlType, out var controlType))
            controlType = SettingControlType.Text;
        ControlType = controlType;

        // Parse persistence
        if (!Enum.TryParse<SettingPersistence>(setting.Persistence, out var persistence))
            persistence = SettingPersistence.Persisted;
        Persistence = persistence;

        // Parse value type - handle simple type names
        ValueType = ParseValueType(setting.ValueType);

        // Initialize reactive subjects
        _labelSubject = new BehaviorSubject<string>(setting.Label);
        _visibilitySubject = new BehaviorSubject<bool>(setting.IsVisible);
        _readOnlySubject = new BehaviorSubject<bool>(setting.IsReadOnly);
        _choicesSubject = new BehaviorSubject<IReadOnlyList<KeyValuePair<string, string>>>(setting.Choices);

        // Parse and set initial value
        var initialValue = ParseValue(savedValue ?? setting.CurrentValue);
        _valueSubject = new BehaviorSubject<object?>(initialValue);

        Label = _labelSubject.AsObservable();
        IsVisible = _visibilitySubject.AsObservable();
        IsReadOnly = _readOnlySubject.AsObservable();
        ValueAsObject = _valueSubject.AsObservable();
        Choices = setting.Choices.Count > 0 ? _choicesSubject.AsObservable() : null;
    }

    public object? GetCurrentValueAsObject()
    {
        return _valueSubject.Value;
    }

    public string GetValueAsString()
    {
        var value = _valueSubject.Value;
        return value switch
        {
            null => string.Empty,
            string s => s,
            bool b => b.ToString(),
            decimal d => d.ToString(),
            int i => i.ToString(),
            double d => d.ToString(),
            _ => value.ToString() ?? string.Empty
        };
    }

    public void SetValueFromObject(object? value)
    {
        _valueSubject.OnNext(value);
    }

    public void SetValueFromString(string? value)
    {
        var parsed = ParseValue(value);
        _valueSubject.OnNext(parsed);
    }

    public string GetCurrentLabel()
    {
        return _labelSubject.Value;
    }

    public bool GetCurrentVisibility()
    {
        return _visibilitySubject.Value;
    }

    public bool GetCurrentReadOnly()
    {
        return _readOnlySubject.Value;
    }

    public IReadOnlyList<KeyValuePair<string, string>>? GetCurrentChoices()
    {
        return _choicesSubject.Value;
    }

    private static Type ParseValueType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return typeof(string);

        return typeName switch
        {
            "String" or "System.String" => typeof(string),
            "Boolean" or "System.Boolean" => typeof(bool),
            "Int32" or "System.Int32" => typeof(int),
            "Decimal" or "System.Decimal" => typeof(decimal),
            "Double" or "System.Double" => typeof(double),
            "TimeSpan" or "System.TimeSpan" => typeof(TimeSpan),
            _ => typeof(string)
        };
    }

    private object? ParseValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return ValueType == typeof(bool) ? false : null;

        try
        {
            if (ValueType == typeof(string))
                return value;
            if (ValueType == typeof(bool))
                return bool.TryParse(value, out var b) ? b : false;
            if (ValueType == typeof(int))
                return int.TryParse(value, out var i) ? i : 0;
            if (ValueType == typeof(decimal))
                return decimal.TryParse(value, out var d) ? d : 0m;
            if (ValueType == typeof(double))
                return double.TryParse(value, out var d) ? d : 0d;
            if (ValueType == typeof(TimeSpan))
                return TimeSpan.TryParse(value, out var ts) ? ts : TimeSpan.Zero;
        }
        catch
        {
            // Ignore parsing errors
        }

        return null;
    }
}