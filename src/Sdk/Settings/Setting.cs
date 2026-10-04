using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Axorith.Sdk.Settings;

/// <summary>
///     Provides static factory methods for creating strongly-typed, reactive settings.
/// </summary>
public abstract class Setting
{
    /// <summary>
    ///     Creates a setting that is rendered as a text input field.
    /// </summary>
    public static Setting<string> AsText(string key, string label, string defaultValue, string? description = null,
        bool isVisible = true, bool isReadOnly = false)
    {
        return CreateString(key, label, defaultValue, SettingControlType.Text, description, isVisible, isReadOnly);
    }

    /// <summary>
    ///     Creates a setting that is rendered as a text area input field.
    /// </summary>
    public static Setting<string> AsTextArea(string key, string label, string defaultValue, string? description = null,
        bool isVisible = true, bool isReadOnly = false)
    {
        return CreateString(key, label, defaultValue, SettingControlType.TextArea, description, isVisible, isReadOnly);
    }

    /// <summary>
    ///     Creates a setting that is rendered as a checkbox.
    /// </summary>
    public static Setting<bool> AsCheckbox(string key, string label, bool defaultValue, string? description = null,
        bool isVisible = true, bool isReadOnly = false)
    {
        return Create(key, label, description, defaultValue, SettingControlType.Checkbox, isVisible, isReadOnly,
            SettingPersistence.Persisted, b => b.ToString(), s => bool.TryParse(s, out var b) && b);
    }

    /// <summary>
    ///     Creates a setting that is rendered as a numeric input field.
    /// </summary>
    public static Setting<decimal> AsNumber(string key, string label, decimal defaultValue, string? description = null,
        bool isVisible = true, bool isReadOnly = false)
    {
        return Create(key, label, description, defaultValue, SettingControlType.Number, isVisible, isReadOnly,
            SettingPersistence.Persisted, d => d.ToString(CultureInfo.InvariantCulture),
            s => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : defaultValue);
    }

    /// <summary>
    ///     Creates a setting that is rendered as a numeric input field bound to an integer value.
    /// </summary>
    public static Setting<int> AsInt(string key, string label, int defaultValue, string? description = null,
        bool isVisible = true, bool isReadOnly = false)
    {
        return Create(key, label, description, defaultValue, SettingControlType.Number, isVisible, isReadOnly,
            SettingPersistence.Persisted, i => i.ToString(CultureInfo.InvariantCulture),
            s => int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : defaultValue);
    }

    /// <summary>
    ///     Creates a setting that is rendered as a numeric input field bound to a double value.
    /// </summary>
    public static Setting<double> AsDouble(string key, string label, double defaultValue, string? description = null,
        bool isVisible = true, bool isReadOnly = false)
    {
        return Create(key, label, description, defaultValue, SettingControlType.Number, isVisible, isReadOnly,
            SettingPersistence.Persisted, d => d.ToString(CultureInfo.InvariantCulture),
            s => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : defaultValue);
    }

    /// <summary>
    ///     Creates a setting that is rendered as a numeric input field bound to a TimeSpan value (in seconds).
    /// </summary>
    public static Setting<TimeSpan> AsTimeSpan(string key, string label, TimeSpan defaultValue,
        string? description = null, bool isVisible = true, bool isReadOnly = false)
    {
        return Create(key, label, description, defaultValue, SettingControlType.Number, isVisible, isReadOnly,
            SettingPersistence.Persisted, ts => ts.TotalSeconds.ToString(CultureInfo.InvariantCulture), s =>
                double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var sec)
                    ? TimeSpan.FromSeconds(sec)
                    : defaultValue);
    }

    /// <summary>
    ///     Creates a setting that is rendered as a dropdown/choice list.
    ///     IMPORTANT: The setting value must always be the KEY from the KeyValuePair, not the display name.
    ///     - Key: The actual value stored in the setting (e.g., "true", "context", "device_id_123")
    ///     - Value: The display text shown to the user (e.g., "On", "Repeat Playlist", "Laptop Speakers")
    ///     - UI binding uses StringValue which contains the key
    ///     - SetChoices() updates available options dynamically
    /// </summary>
    public static Setting<string> AsChoice(string key, string label, string defaultValue,
        IReadOnlyList<KeyValuePair<string, string>> initialChoices, string? description = null, bool isVisible = true,
        bool isReadOnly = false)
    {
        var setting = CreateString(key, label, defaultValue, SettingControlType.Choice, description, isVisible,
            isReadOnly);
        setting.InitializeChoices(initialChoices);
        return setting;
    }

    /// <summary>
    ///     Creates a setting that is rendered as a list of checkboxes (multiple selection).
    ///     The value is a list of selected keys.
    /// </summary>
    public static Setting<List<string>> AsMultiChoice(string key, string label, List<string> defaultValues,
        IReadOnlyList<KeyValuePair<string, string>> initialChoices, string? description = null, bool isVisible = true,
        bool isReadOnly = false)
    {
        var setting = Create(
            key,
            label,
            description,
            defaultValues,
            SettingControlType.MultiChoice,
            isVisible,
            isReadOnly,
            SettingPersistence.Persisted,
            list => string.Join("|", list),
            s => string.IsNullOrEmpty(s) ? [] : [.. s.Split('|', StringSplitOptions.RemoveEmptyEntries)]
        );

        setting.InitializeChoices(initialChoices);
        return setting;
    }

    /// <summary>
    ///     Creates a setting that is rendered as a password/secret input field.
    ///     Secret settings are NEVER persisted to preset JSON files. Instead, their values are stored
    ///     in SecureStorage (Windows DPAPI) and restored when the session starts.
    ///     This ensures sensitive data (API tokens, passwords) never appear in plaintext on disk.
    /// </summary>
    public static Setting<string> AsSecret(string key, string label, string? description = null, bool isVisible = true,
        bool isReadOnly = false)
    {
        return CreateString(key, label, string.Empty, SettingControlType.Secret, description, isVisible, isReadOnly,
            SettingPersistence.Ephemeral);
    }

    /// <summary>
    ///     Creates a setting that is rendered as a text field with a file browser button.
    /// </summary>
    public static Setting<string> AsFilePicker(string key, string label, string defaultValue, string? filter = null,
        string? description = null, bool isVisible = true, bool isReadOnly = false, bool useHistory = true)
    {
        return CreateString(key, label, defaultValue, SettingControlType.FilePicker, description, isVisible, isReadOnly,
            filter: filter, hasHistory: useHistory);
    }

    /// <summary>
    ///     Creates a setting that is rendered as a text field with a directory browser button.
    /// </summary>
    public static Setting<string> AsDirectoryPicker(string key, string label, string defaultValue,
        string? description = null, bool isVisible = true, bool isReadOnly = false, bool useHistory = true)
    {
        return CreateString(key, label, defaultValue, SettingControlType.DirectoryPicker, description, isVisible,
            isReadOnly, hasHistory: useHistory);
    }

    private static Setting<string> CreateString(string key, string label, string defaultValue,
        SettingControlType controlType, string? description, bool isVisible, bool isReadOnly,
        SettingPersistence persistence = SettingPersistence.Persisted, string? filter = null, bool hasHistory = false) =>
        Create(key, label, description, defaultValue, controlType, isVisible, isReadOnly, persistence, s => s,
            s => s ?? defaultValue, filter, hasHistory);

    private static Setting<T> Create<T>(string key, string label, string? description, T defaultValue,
        SettingControlType controlType, bool isVisible, bool isReadOnly, SettingPersistence persistence,
        Func<T, string> serializer, Func<string?, T> deserializer, string? filter = null, bool hasHistory = false) =>
        new(key, label, description, defaultValue, controlType, isVisible, isReadOnly, persistence, serializer,
            deserializer) { Filter = filter, HasHistory = hasHistory };
}

/// <summary>
///     A strongly-typed reactive module setting with observable value and UI metadata.
/// </summary>
public class Setting<T> : ISetting, IDisposable
{
    private readonly BehaviorSubject<T> _value;
    private readonly BehaviorSubject<string> _label;
    private readonly BehaviorSubject<bool> _isVisible;
    private readonly BehaviorSubject<bool> _isReadOnly;
    private BehaviorSubject<IReadOnlyList<KeyValuePair<string, string>>>? _choices;

    private readonly Func<T, string> _serializer;
    private readonly Func<string?, T> _deserializer;

    /// <inheritdoc />
    public string Key { get; }

    /// <inheritdoc />
    public string? Description { get; }

    /// <inheritdoc />
    public SettingControlType ControlType { get; }

    /// <inheritdoc />
    public Type ValueType => typeof(T);

    /// <inheritdoc />
    public SettingPersistence Persistence { get; }

    /// <inheritdoc />
    public string? Filter { get; internal init; }

    /// <inheritdoc />
    public IObservable<string> Label => _label.AsObservable();

    /// <summary>
    ///     An observable that emits the setting's value whenever it changes.
    /// </summary>
    public IObservable<T> Value => _value.AsObservable();

    /// <inheritdoc />
    public IObservable<bool> IsVisible => _isVisible.AsObservable();

    /// <inheritdoc />
    public IObservable<bool> IsReadOnly => _isReadOnly.AsObservable();

    /// <inheritdoc />
    public IObservable<IReadOnlyList<KeyValuePair<string, string>>>? Choices => _choices?.AsObservable();

    /// <inheritdoc />
    public IObservable<object?> ValueAsObject => _value.Select(v => (object?)v);

    /// <inheritdoc />
    public bool HasHistory { get; internal init; }

    object? ISetting.GetCurrentValueAsObject() => _value.Value;

    string ISetting.GetValueAsString() => _serializer(_value.Value);

    void ISetting.SetValueFromObject(object? value)
    {
        switch (value)
        {
            case T castValue:
                _value.OnNext(castValue);
                return;
            case string s:
                _value.OnNext(_deserializer(s));
                return;
            default:
                try
                {
                    if (typeof(T) == typeof(TimeSpan))
                    {
                        if (value is IConvertible)
                        {
                            var seconds = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                            _value.OnNext((T)(object)TimeSpan.FromSeconds(seconds));
                            return;
                        }
                    }


                    if (value is IConvertible)
                    {
                        var converted = (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
                        _value.OnNext(converted);
                    }
                }
                catch
                {
                    // Swallow conversion errors to avoid crashing UI; callers still have ValueAsObject binding
                }

                break;
        }
    }

    internal Setting(string key, string label, string? description, T defaultValue, SettingControlType controlType,
        bool isVisible, bool isReadOnly, SettingPersistence persistence, Func<T, string> serializer,
        Func<string?, T> deserializer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(deserializer);

        Key = key;
        Description = description;
        ControlType = controlType;
        Persistence = persistence;
        _serializer = serializer;
        _deserializer = deserializer;

        _label = new BehaviorSubject<string>(label);
        _value = new BehaviorSubject<T>(defaultValue);
        _isVisible = new BehaviorSubject<bool>(isVisible);
        _isReadOnly = new BehaviorSubject<bool>(isReadOnly);
    }

    internal void InitializeChoices(IReadOnlyList<KeyValuePair<string, string>> initialChoices)
    {
        ArgumentNullException.ThrowIfNull(initialChoices);
        _choices = new BehaviorSubject<IReadOnlyList<KeyValuePair<string, string>>>(initialChoices);
    }

    /// <summary>
    ///     Gets the current value of the setting synchronously.
    /// </summary>
    public T GetCurrentValue() => _value.Value;

    /// <summary>
    ///     Sets the value of the setting, notifying all subscribers.
    /// </summary>
    public void SetValue(T value) => _value.OnNext(value);

    /// <summary>
    ///     Dynamically updates the setting's label, notifying the UI.
    /// </summary>
    public void SetLabel(string newLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newLabel);
        _label.OnNext(newLabel);
    }

    /// <summary>
    ///     Dynamically updates the available choices for a Choice setting.
    /// </summary>
    public void SetChoices(IReadOnlyList<KeyValuePair<string, string>> newChoices)
    {
        ArgumentNullException.ThrowIfNull(newChoices);
        // Allow empty lists to support clearing dropdowns or error states
        _choices?.OnNext(newChoices);
    }

    /// <summary>
    ///     Dynamically updates the setting's visibility, notifying the UI.
    /// </summary>
    public void SetVisibility(bool isVisible) => _isVisible.OnNext(isVisible);

    /// <summary>
    ///     Dynamically updates the setting's read-only state, notifying the UI.
    /// </summary>
    public void SetReadOnly(bool isReadOnly) => _isReadOnly.OnNext(isReadOnly);

    /// <inheritdoc />
    void ISetting.SetValueFromString(string? value) => _value.OnNext(_deserializer(value));

    /// <inheritdoc />
    string ISetting.GetCurrentLabel() => _label.Value;

    /// <inheritdoc />
    bool ISetting.GetCurrentVisibility() => _isVisible.Value;

    /// <inheritdoc />
    bool ISetting.GetCurrentReadOnly() => _isReadOnly.Value;

    /// <inheritdoc />
    IReadOnlyList<KeyValuePair<string, string>>? ISetting.GetCurrentChoices() => _choices?.Value;

    /// <summary>
    ///     Disposes the setting and releases all resources.
    /// </summary>
    public void Dispose()
    {
        _value.Dispose();
        _label.Dispose();
        _isVisible.Dispose();
        _isReadOnly.Dispose();
        _choices?.Dispose();
        GC.SuppressFinalize(this);
    }
}
