namespace Axorith.Sdk.Settings;

/// <summary>
///     Represents a module setting that allows the user to select a directory path,
///     typically rendered as a TextBox with a "Browse..." button in the UI.
/// </summary>
public class DirectoryPickerSetting : SettingBase
{
    /// <summary>
    ///     Gets the default path for this setting.
    /// </summary>
    public string DefaultValue { get; }

    /// <inheritdoc />
    public override object DefaultValueObject => DefaultValue;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DirectoryPickerSetting" /> class.
    /// </summary>
    /// <param name="key">The unique, machine-readable key for the setting.</param>
    /// <param name="label">The human-readable name displayed in the UI.</param>
    /// <param name="description">An optional description for a tooltip.</param>
    /// <param name="defaultValue">The default directory path for this setting.</param>
    public DirectoryPickerSetting(
        string key,
        string label,
        string? description = null,
        string defaultValue = "")
        : base(key, label, description)
    {
        DefaultValue = defaultValue;
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