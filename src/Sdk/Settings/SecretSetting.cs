namespace Axorith.Sdk.Settings;

/// <summary>
/// Represents a module setting that accepts a string of text.
/// Can be rendered as either a single-line TextBox or a multi-line TextArea in the UI.
/// </summary>
public class SecretSetting : SettingBase
{
    /// <summary>
    /// Gets the default value for this setting, boxed as an object.
    /// </summary>
    public override object DefaultValueObject => string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretSetting"/> class.
    /// </summary>
    /// <param name="key">The unique, machine-readable key for the setting.</param>
    /// <param name="label">The human-readable name displayed in the UI.</param>
    /// <param name="description">An optional description for a tooltip.</param>
    public SecretSetting(string key, string label, string? description = null) 
        : base(key, label, description) 
    { 
    }

    /// <summary>
    /// Sets the <see cref="ISettingViewModel.StringValue"/> property on the provided ViewModel.
    /// If the saved value is null, the default value is used.
    /// </summary>
    /// <param name="vm">The ViewModel to initialize.</param>
    /// <param name="savedValue">The raw string value loaded from storage.</param>
    public override void InitializeViewModel(ISettingViewModel vm, string? savedValue)
    {
        vm.StringValue = savedValue ?? string.Empty;
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