namespace Axorith.Sdk.Settings;

/// <summary>
///     Represents a module setting that accepts a true/false value,
///     typically rendered as a CheckBox in the UI.
/// </summary>
/// <param name="key">The unique, machine-readable key for the setting.</param>
/// <param name="label">The human-readable name displayed in the UI.</param>
/// <param name="description">An optional description for a tooltip.</param>
/// <param name="defaultValue">The default boolean value for this setting.</param>
public class CheckboxSetting(string key, string label, string? description = null, bool defaultValue = false)
    : SettingBase(key, label, description)
{
    /// <summary>
    ///     Gets the default value for this setting as a boolean.
    /// </summary>
    private bool DefaultValue { get; } = defaultValue;

    /// <summary>
    ///     Gets the default value for this setting, boxed as an object.
    /// </summary>
    public override object DefaultValueObject => DefaultValue;

    /// <summary>
    ///     Parses the saved string value into a boolean and sets the <see cref="ISettingViewModel.BoolValue" /> property on
    ///     the provided ViewModel.
    ///     If parsing fails or the saved value is null, the default value is used.
    /// </summary>
    /// <param name="vm">The ViewModel to initialize.</param>
    /// <param name="savedValue">The raw string value loaded from storage (e.g., "True" or "False").</param>
    public override void InitializeViewModel(ISettingViewModel vm, string? savedValue)
    {
        vm.BoolValue = bool.TryParse(savedValue, out var b) ? b : DefaultValue;
    }

    /// <summary>
    ///     Retrieves the current boolean value from the <see cref="ISettingViewModel.BoolValue" /> property
    ///     and formats it as a string ("True" or "False") for serialization.
    /// </summary>
    /// <param name="vm">The ViewModel containing the current user-provided value.</param>
    /// <returns>A string representation of the current value, suitable for saving in JSON.</returns>
    public override string GetValueFromViewModel(ISettingViewModel vm)
    {
        return vm.BoolValue.ToString();
    }
}