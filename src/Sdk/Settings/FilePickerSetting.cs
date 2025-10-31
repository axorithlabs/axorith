namespace Axorith.Sdk.Settings;

/// <summary>
///     Represents a module setting that allows the user to select a file path,
///     typically rendered as a TextBox with a "Browse..." button in the UI.
/// </summary>
public class FilePickerSetting : SettingBase
{
    /// <summary>
    ///     Gets the default path for this setting.
    /// </summary>
    public string DefaultValue { get; }

    /// <summary>
    ///     Gets the filter string for the file picker dialog.
    ///     Example: "Text files (*.txt)|*.txt|All files (*.*)|*.*"
    /// </summary>
    public string? Filter { get; }

    /// <inheritdoc />
    public override object DefaultValueObject => DefaultValue;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FilePickerSetting" /> class.
    /// </summary>
    /// <param name="key">The unique, machine-readable key for the setting.</param>
    /// <param name="label">The human-readable name displayed in the UI.</param>
    /// <param name="description">An optional description for a tooltip.</param>
    /// <param name="defaultValue">The default file path for this setting.</param>
    /// <param name="filter">An optional filter string for the file dialog.</param>
    public FilePickerSetting(
        string key,
        string label,
        string? description = null,
        string defaultValue = "",
        string? filter = null)
        : base(key, label, description)
    {
        DefaultValue = defaultValue;
        Filter = filter;
    }

    /// <summary>
    ///     Sets the <see cref="ISettingViewModel.StringValue" /> property on the provided ViewModel.
    ///     If the saved value is null, the default value is used.
    /// </summary>
    /// <param name="vm">The ViewModel to initialize.</param>
    /// <param name="savedValue">The raw string value loaded from storage.</param>
    public override void InitializeViewModel(ISettingViewModel vm, string? savedValue)
    {
        vm.StringValue = savedValue ?? DefaultValue;
    }

    /// <summary>
    ///     Retrieves the current string value from the <see cref="ISettingViewModel.StringValue" /> property.
    /// </summary>
    /// <param name="vm">The ViewModel containing the current user-provided value.</param>
    /// <returns>The current string value, ready for serialization.</returns>
    public override string GetValueFromViewModel(ISettingViewModel vm)
    {
        return vm.StringValue;
    }

    /// <inheritdoc />
    public override string GetDefaultValueAsString()
    {
        return DefaultValue;
    }
}