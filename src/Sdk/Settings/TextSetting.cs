namespace Axorith.Sdk.Settings;

/// <summary>
/// Represents a module setting that accepts a string of text.
/// Can be rendered as either a single-line TextBox or a multi-line TextArea in the UI.
/// </summary>
public class TextSetting : SettingBase
{
    /// <summary>
    /// Gets the default value for this setting as a string.
    /// </summary>
    public string DefaultValue { get; }

    /// <summary>
    /// Gets a value indicating whether the UI should render a multi-line text area.
    /// </summary>
    public bool IsMultiLine { get; }

    /// <summary>
    /// Gets the default value for this setting, boxed as an object.
    /// </summary>
    public override object DefaultValueObject => DefaultValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextSetting"/> class.
    /// </summary>
    /// <param name="key">The unique, machine-readable key for the setting.</param>
    /// <param name="label">The human-readable name displayed in the UI.</param>
    /// <param name="description">An optional description for a tooltip.</param>
    /// <param name="defaultValue">The default string value for this setting.</param>
    /// <param name="isMultiLine">If true, the UI should suggest a multi-line text input.</param>
    public TextSetting(string key, string label, string? description = null, string defaultValue = "", bool isMultiLine = false) 
        : base(key, label, description) 
    { 
        DefaultValue = defaultValue;
        IsMultiLine = isMultiLine;
    }

    /// <summary>
    /// Sets the <see cref="ISettingViewModel.StringValue"/> property on the provided ViewModel.
    /// If the saved value is null, the default value is used.
    /// </summary>
    /// <param name="vm">The ViewModel to initialize.</param>
    /// <param name="savedValue">The raw string value loaded from storage.</param>
    public override void InitializeViewModel(ISettingViewModel vm, string? savedValue)
    {
        vm.StringValue = savedValue ?? DefaultValue;
    }

    /// <summary>
    /// Retrieves the current string value from the <see cref="ISettingViewModel.StringValue"/> property.
    /// </summary>
    /// <param name="vm">The ViewModel containing the current user-provided value.</param>
    /// <returns>The current string value, ready for serialization.</returns>
    public override string GetValueFromViewModel(ISettingViewModel vm)
    {
        return vm.StringValue;
    }
}