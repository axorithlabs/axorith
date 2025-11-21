namespace Axorith.Sdk.Settings;

/// <summary>
///     Represents the public contract for a reactive setting, consumed by the UI and Core.
/// </summary>
public interface ISetting
{
    /// <summary>
    ///     Gets the unique, machine-readable key for this setting.
    /// </summary>
    string Key { get; }

    /// <summary>
    ///     Gets an observable that emits the setting's display label.
    /// </summary>
    IObservable<string> Label { get; }

    /// <summary>
    ///     Gets the optional description for the setting, used for tooltips.
    /// </summary>
    string? Description { get; }

    /// <summary>
    ///     Gets the type of UI control that should be used to render this setting.
    /// </summary>
    SettingControlType ControlType { get; }

    /// <summary>
    ///     Gets the underlying value type of this setting (e.g., typeof(string), typeof(bool), typeof(int)).
    /// </summary>
    Type ValueType { get; }

    /// <summary>
    ///     Describes if and how the setting is persisted into presets.
    /// </summary>
    SettingPersistence Persistence { get; }

    // Reactive properties for the UI to bind to.
    /// <summary>
    ///     Gets an observable that emits the setting's value as a boxed object.
    ///     This is used by the UI to subscribe to value changes without knowing the generic type <c>T</c>.
    /// </summary>
    IObservable<object?> ValueAsObject { get; }

    /// <summary>
    ///     Returns the current value boxed as object.
    /// </summary>
    object? GetCurrentValueAsObject();

    /// <summary>
    ///     Returns the current value serialized as string using the setting's serializer.
    /// </summary>
    string GetValueAsString();

    /// <summary>
    ///     Sets the value from a boxed object, attempting safe conversion when possible.
    /// </summary>
    /// <param name="value">The new value as object.</param>
    void SetValueFromObject(object? value);

    /// <summary>
    ///     Returns the current label value synchronously (snapshot).
    /// </summary>
    string GetCurrentLabel();

    /// <summary>
    ///     Returns the current visibility state synchronously (snapshot).
    /// </summary>
    bool GetCurrentVisibility();

    /// <summary>
    ///     Returns the current read-only state synchronously (snapshot).
    /// </summary>
    bool GetCurrentReadOnly();

    /// <summary>
    ///     Returns the current choices list synchronously (snapshot).
    /// </summary>
    IReadOnlyList<KeyValuePair<string, string>>? GetCurrentChoices();

    /// <summary>
    ///     Gets an observable that controls the visibility of the setting in the UI.
    /// </summary>
    IObservable<bool> IsVisible { get; }

    /// <summary>
    ///     Gets an observable that controls the read-only state of the setting in the UI.
    /// </summary>
    IObservable<bool> IsReadOnly { get; }

    /// <summary>
    ///     For 'Choice' settings, gets an observable that provides the list of available options.
    ///     Returns <c>null</c> for other setting types.
    /// </summary>
    IObservable<IReadOnlyList<KeyValuePair<string, string>>>? Choices { get; }

    /// <summary>
    ///     For 'FilePicker' settings, gets the filter string for the file dialog.
    /// </summary>
    string? Filter { get; }
    
    /// <summary>
    ///     Indicates if the client should remember previous values for this setting.
    /// </summary>
    bool HasHistory { get; }

    /// <summary>
    ///     Used by the Core to populate the setting's value from a saved preset.
    /// </summary>
    /// <param name="value">The string value from the preset file.</param>
    void SetValueFromString(string? value);
}