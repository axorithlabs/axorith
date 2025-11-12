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
    /// <example>
    ///     _greetingMessage = Setting.AsText(
    ///     key: "GreetingMessage",
    ///     label: "Greeting Message",
    ///     description: "This message will be logged on session start.",
    ///     defaultValue: "Hello from TestModule!"
    ///     );
    /// </example>
    /// <param name="key">The unique key for this setting.</param>
    /// <param name="label">The display label for the UI.</param>
    /// <param name="defaultValue">The default text value.</param>
    /// <param name="description">An optional description for a tooltip.</param>
    /// <param name="isVisible">The initial visibility of the control.</param>
    /// <param name="isReadOnly">The initial read-only state of the control.</param>
    /// <returns>A new reactive text setting.</returns>
    public static Setting<string> AsText(string key, string label, string defaultValue, string? description = null,
        bool isVisible = true, bool isReadOnly = false)
    {
        return new Setting<string>(key, label, description, defaultValue, SettingControlType.Text, isVisible,
            isReadOnly, SettingPersistence.Persisted, s => s, s => s ?? defaultValue);
    }

    /// <summary>
    ///     Creates a setting that is rendered as a text area input field.
    /// </summary>
    /// <example>
    ///     _blockedSites = Setting.AsTextArea(
    ///     key: "BlockedSites",
    ///     label: "Sites to Block",
    ///     description: "A comma-separated list of domains to block (e.g., youtube.com, twitter.com, reddit.com).",
    ///     defaultValue: "youtube.com, twitter.com, reddit.com"
    ///     );
    /// </example>
    /// <param name="key">The unique key for this setting.</param>
    /// <param name="label">The display label for the UI.</param>
    /// <param name="defaultValue">The default text value.</param>
    /// <param name="description">An optional description for a tooltip.</param>
    /// <param name="isVisible">The initial visibility of the control.</param>
    /// <param name="isReadOnly">The initial read-only state of the control.</param>
    /// <returns>A new reactive text setting.</returns>
    public static Setting<string> AsTextArea(string key, string label, string defaultValue, string? description = null,
        bool isVisible = true, bool isReadOnly = false)
    {
        return new Setting<string>(key, label, description, defaultValue, SettingControlType.TextArea, isVisible,
            isReadOnly, SettingPersistence.Persisted, s => s, s => s ?? defaultValue);
    }

    /// <summary>
    ///     Creates a setting that is rendered as a checkbox.
    /// </summary>
    /// <example>
    ///     _enableExtraLogging = Setting.AsCheckbox(
    ///     key: "EnableExtraLogging",
    ///     label: "Enable Extra Logging",
    ///     description: "If checked, the module will log a countdown.",
    ///     defaultValue: true
    ///     );
    /// </example>
    /// <param name="key">The unique key for this setting.</param>
    /// <param name="label">The display label for the UI.</param>
    /// <param name="defaultValue">The default boolean value (checked/unchecked).</param>
    /// <param name="description">An optional description for a tooltip.</param>
    /// <param name="isVisible">The initial visibility of the control.</param>
    /// <param name="isReadOnly">The initial read-only state of the control.</param>
    /// <returns>A new reactive checkbox setting.</returns>
    public static Setting<bool> AsCheckbox(string key, string label, bool defaultValue, string? description = null,
        bool isVisible = true, bool isReadOnly = false)
    {
        return new Setting<bool>(key, label, description, defaultValue, SettingControlType.Checkbox, isVisible,
            isReadOnly, SettingPersistence.Persisted, b => b.ToString(), s => bool.TryParse(s, out var b) && b);
    }

    /// <summary>
    ///     Creates a setting that is rendered as a numeric input field.
    /// </summary>
    /// <example>
    ///     _workDurationSeconds = Setting.AsNumber(
    ///     key: "WorkDurationSeconds",
    ///     label: "Work Duration (sec)",
    ///     description: "How long the module should simulate work.",
    ///     defaultValue: 5
    ///     );
    /// </example>
    /// <param name="key">The unique key for this setting.</param>
    /// <param name="label">The display label for the UI.</param>
    /// <param name="defaultValue">The default numeric value.</param>
    /// <param name="description">An optional description for a tooltip.</param>
    /// <param name="isVisible">The initial visibility of the control.</param>
    /// <param name="isReadOnly">The initial read-only state of the control.</param>
    /// <returns>A new reactive number setting.</returns>
    public static Setting<decimal> AsNumber(string key, string label, decimal defaultValue, string? description = null,
        bool isVisible = true, bool isReadOnly = false)
    {
        return new Setting<decimal>(key, label, description, defaultValue, SettingControlType.Number, isVisible,
            isReadOnly, SettingPersistence.Persisted, d => d.ToString(CultureInfo.InvariantCulture),
            s => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : defaultValue);
    }

    /// <summary>
    ///     Creates a setting that is rendered as a numeric input field bound to an integer value.
    /// </summary>
    /// <param name="key">The unique key for this setting.</param>
    /// <param name="label">The display label for the UI.</param>
    /// <param name="defaultValue">The default integer value.</param>
    /// <param name="description">An optional description for a tooltip.</param>
    /// <param name="isVisible">The initial visibility of the control.</param>
    /// <param name="isReadOnly">The initial read-only state of the control.</param>
    /// <returns>A new reactive integer setting.</returns>
    public static Setting<int> AsInt(string key, string label, int defaultValue, string? description = null,
        bool isVisible = true, bool isReadOnly = false)
    {
        return new Setting<int>(key, label, description, defaultValue, SettingControlType.Number, isVisible,
            isReadOnly, SettingPersistence.Persisted, i => i.ToString(CultureInfo.InvariantCulture),
            s => int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : defaultValue);
    }

    /// <summary>
    ///     Creates a setting that is rendered as a numeric input field bound to a double value.
    /// </summary>
    /// <param name="key">The unique key for this setting.</param>
    /// <param name="label">The display label for the UI.</param>
    /// <param name="defaultValue">The default double value.</param>
    /// <param name="description">An optional description for a tooltip.</param>
    /// <param name="isVisible">The initial visibility of the control.</param>
    /// <param name="isReadOnly">The initial read-only state of the control.</param>
    /// <returns>A new reactive double setting.</returns>
    public static Setting<double> AsDouble(string key, string label, double defaultValue, string? description = null,
        bool isVisible = true, bool isReadOnly = false)
    {
        return new Setting<double>(key, label, description, defaultValue, SettingControlType.Number, isVisible,
            isReadOnly, SettingPersistence.Persisted, d => d.ToString(CultureInfo.InvariantCulture),
            s => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : defaultValue);
    }

    /// <summary>
    ///     Creates a setting that is rendered as a numeric input field bound to a TimeSpan value (in seconds).
    /// </summary>
    /// <param name="key">The unique key for this setting.</param>
    /// <param name="label">The display label for the UI.</param>
    /// <param name="defaultValue">The default TimeSpan value.</param>
    /// <param name="description">An optional description for a tooltip.</param>
    /// <param name="isVisible">The initial visibility of the control.</param>
    /// <param name="isReadOnly">The initial read-only state of the control.</param>
    /// <returns>A new reactive TimeSpan setting.</returns>
    public static Setting<TimeSpan> AsTimeSpan(string key, string label, TimeSpan defaultValue,
        string? description = null, bool isVisible = true, bool isReadOnly = false)
    {
        return new Setting<TimeSpan>(key, label, description, defaultValue, SettingControlType.Number, isVisible,
            isReadOnly, SettingPersistence.Persisted, ts => ts.TotalSeconds.ToString(CultureInfo.InvariantCulture), s =>
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
    /// <param name="key">The unique key for this setting.</param>
    /// <param name="label">The label to display for this setting.</param>
    /// <param name="defaultValue">The initial selected key (must match a key in initialChoices).</param>
    /// <param name="initialChoices">The list of key-value pairs representing the available options.</param>
    /// <param name="description">An optional description for a tooltip.</param>
    /// <param name="isVisible">The initial visibility of the control.</param>
    /// <param name="isReadOnly">The initial read-only state of the control.</param>
    /// <returns>A new reactive choice setting.</returns>
    public static Setting<string> AsChoice(string key, string label, string defaultValue,
        IReadOnlyList<KeyValuePair<string, string>> initialChoices, string? description = null, bool isVisible = true,
        bool isReadOnly = false)
    {
        var setting = new Setting<string>(key, label, description, defaultValue, SettingControlType.Choice, isVisible,
            isReadOnly, SettingPersistence.Persisted, s => s, s => s ?? defaultValue);
        setting.InitializeChoices(initialChoices);
        return setting;
    }

    /// <summary>
    ///     Creates a setting that is rendered as a password/secret input field.
    ///     Secret settings are NEVER persisted to preset JSON files. Instead, their values are stored
    ///     in SecureStorage (Windows DPAPI) and restored when the session starts.
    ///     This ensures sensitive data (API tokens, passwords) never appear in plaintext on disk.
    /// </summary>
    /// <example>
    ///     _spotifyRefreshToken = Setting.AsSecret(
    ///     key: "RefreshToken",
    ///     label: "Spotify Refresh Token",
    ///     description: "Automatically managed by the authentication system."
    ///     );
    /// </example>
    /// <param name="key">The unique key for this setting. Used as the SecureStorage key.</param>
    /// <param name="label">The display label for the UI.</param>
    /// <param name="description">An optional description for a tooltip.</param>
    /// <param name="isVisible">The initial visibility of the control.</param>
    /// <param name="isReadOnly">The initial read-only state of the control.</param>
    /// <returns>A new reactive secret setting with Ephemeral persistence (not saved to presets).</returns>
    public static Setting<string> AsSecret(string key, string label, string? description = null, bool isVisible = true,
        bool isReadOnly = false)
    {
        return new Setting<string>(key, label, description, string.Empty, SettingControlType.Secret, isVisible,
            isReadOnly, SettingPersistence.Ephemeral, s => s, s => s ?? string.Empty);
    }

    /// <summary>
    ///     Creates a setting that is rendered as a text field with a file browser button.
    /// </summary>
    /// <example>
    ///     _inputFile = Setting.AsFilePicker(
    ///     key: "InputFile",
    ///     label: "Input File",
    ///     description: "Select a configuration file.",
    ///     defaultValue: "",
    ///     filter: "JSON files (*.json)|*.json|All files (*.*)|*.*"
    ///     );
    /// </example>
    /// <param name="key">The unique key for this setting.</param>
    /// <param name="label">The display label for the UI.</param>
    /// <param name="defaultValue">The default file path.</param>
    /// <param name="filter">The file dialog filter string (e.g., "JSON files (*.json)|*.json|All files (*.*)|*.*").</param>
    /// <param name="description">An optional description for a tooltip.</param>
    /// <param name="isVisible">The initial visibility of the control.</param>
    /// <param name="isReadOnly">The initial read-only state of the control.</param>
    /// <returns>A new reactive file picker setting.</returns>
    public static Setting<string> AsFilePicker(string key, string label, string defaultValue, string? filter = null,
        string? description = null, bool isVisible = true, bool isReadOnly = false)
    {
        return new Setting<string>(key, label, description, defaultValue, SettingControlType.FilePicker, isVisible,
            isReadOnly, SettingPersistence.Persisted, s => s, s => s ?? defaultValue) { Filter = filter };
    }

    /// <summary>
    ///     Creates a setting that is rendered as a text field with a directory browser button.
    ///     <example>
    ///         _outputDirectory = Setting.AsDirectoryPicker(
    ///         key: "OutputDirectory",
    ///         label: "Output Directory",
    ///         description: "Select a directory for output files.",
    ///         defaultValue: Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
    ///         );
    ///     </example>
    /// </summary>
    /// <param name="key">The unique key for this setting.</param>
    /// <param name="label">The display label for the UI.</param>
    /// <param name="defaultValue">The default directory path.</param>
    /// <param name="description">An optional description for a tooltip.</param>
    /// <param name="isVisible">The initial visibility of the control.</param>
    /// <param name="isReadOnly">The initial read-only state of the control.</param>
    /// <returns>A new reactive directory picker setting.</returns>
    public static Setting<string> AsDirectoryPicker(string key, string label, string defaultValue,
        string? description = null, bool isVisible = true, bool isReadOnly = false)
    {
        return new Setting<string>(key, label, description, defaultValue, SettingControlType.DirectoryPicker, isVisible,
            isReadOnly, SettingPersistence.Persisted, s => s, s => s ?? defaultValue);
    }

    /// <summary>
    ///     Creates a setting that is rendered as a clickable button.
    ///     This setting is boolean-based, where a `true` value represents a click event.
    ///     The module logic is responsible for reacting to the `true` value and resetting it to `false`.
    /// </summary>
    /// <param name="key">The unique key for this setting.</param>
    /// <param name="label">The text to display on the button.</param>
    /// <param name="description">An optional description for a tooltip.</param>
    /// <param name="isVisible">The initial visibility of the control.</param>
    /// <param name="isReadOnly">The initial read-only state of the control.</param>
    /// <returns>A new reactive button setting.</returns>
    [Obsolete("Use IAction instead of AsButton; actions are non-persisted by design.")]
    public static Setting<bool> AsButton(string key, string label, string? description = null, bool isVisible = true,
        bool isReadOnly = false)
    {
        return new Setting<bool>(key, label, description, false, SettingControlType.Button, isVisible, isReadOnly,
            SettingPersistence.Transient, b => b.ToString(), s => bool.TryParse(s, out var b) && b);
    }
}

/// <summary>
///     A strongly-typed, reactive representation of a single module setting.
///     This class encapsulates its value, UI metadata, and reactive state.
///     THREAD-SAFETY:
///     - All methods (SetValue, SetLabel, SetVisibility, etc.) are thread-safe and can be called from any thread.
///     - Observable subscriptions should use ObserveOn(RxApp.MainThreadScheduler) in UI code to marshal to UI thread.
///     - SetValueFromString/SetValueFromObject can be called from background threads (e.g., during async loading).
///     - Reactive subjects are thread-safe by default and emit on the calling thread.
/// </summary>
/// <typeparam name="T">The underlying type of the setting's value (e.g., string, bool, decimal).</typeparam>
public class Setting<T> : ISetting
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
    public string? Filter { get; internal set; }

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

    object? ISetting.GetCurrentValueAsObject()
    {
        return _value.Value;
    }

    string ISetting.GetValueAsString()
    {
        return _serializer(_value.Value);
    }

    void ISetting.SetValueFromObject(object? value)
    {
        // Direct assignment when types match
        if (value is T castValue)
        {
            _value.OnNext(castValue);
            return;
        }

        // Strings are fed through existing deserializer for consistency
        if (value is string s)
        {
            _value.OnNext(_deserializer(s));
            return;
        }

        try
        {
            // Special cases
            if (typeof(T) == typeof(TimeSpan))
                // Interpret as seconds
                if (value is IConvertible)
                {
                    var seconds = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    _value.OnNext((T)(object)TimeSpan.FromSeconds(seconds));
                    return;
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
    public T GetCurrentValue()
    {
        return _value.Value;
    }

    /// <summary>
    ///     Sets the value of the setting, notifying all subscribers.
    /// </summary>
    public void SetValue(T value)
    {
        _value.OnNext(value);
    }

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
    public void SetVisibility(bool isVisible)
    {
        _isVisible.OnNext(isVisible);
    }

    /// <summary>
    ///     Dynamically updates the setting's read-only state, notifying the UI.
    /// </summary>
    public void SetReadOnly(bool isReadOnly)
    {
        _isReadOnly.OnNext(isReadOnly);
    }

    /// <inheritdoc />
    void ISetting.SetValueFromString(string? value)
    {
        _value.OnNext(_deserializer(value));
    }

    /// <inheritdoc />
    string ISetting.GetCurrentLabel()
    {
        return _label.Value;
    }

    /// <inheritdoc />
    bool ISetting.GetCurrentVisibility()
    {
        return _isVisible.Value;
    }

    /// <inheritdoc />
    bool ISetting.GetCurrentReadOnly()
    {
        return _isReadOnly.Value;
    }

    /// <inheritdoc />
    IReadOnlyList<KeyValuePair<string, string>>? ISetting.GetCurrentChoices()
    {
        return _choices?.Value;
    }
}