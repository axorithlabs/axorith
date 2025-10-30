namespace Axorith.Sdk.Settings;

/// <summary>
///     Represents a setting that allows the user to choose from a predefined list of options.
///     Typically rendered as a ComboBox or dropdown in the UI.
/// </summary>
public class ChoiceSetting : SettingBase
{
    /// <summary>
    ///     Gets the list of available choices for the user.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Choices { get; }

    /// <summary>
    ///     Gets the default value (key) for this setting.
    /// </summary>
    public string DefaultValue { get; }

    /// <inheritdoc />
    public override object DefaultValueObject => DefaultValue;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ChoiceSetting" /> class.
    /// </summary>
    /// <param name="key">The unique, machine-readable key for the setting.</param>
    /// <param name="label">The human-readable name displayed in the UI.</param>
    /// <param name="choices">
    ///     A read-only list of key-value pairs representing the available options. The 'Key' is the stored
    ///     value, and the 'Value' is the display text.
    /// </param>
    /// <param name="defaultValue">The key of the default choice from the 'choices' list.</param>
    /// <param name="description">An optional, more detailed description for the setting, often used as a tooltip in the UI.</param>
    public ChoiceSetting(string key, string label, IReadOnlyList<KeyValuePair<string, string>> choices,
        string defaultValue, string? description = null)
        : base(key, label, description)
    {
        Choices = choices;
        DefaultValue = defaultValue;
    }

    /// <inheritdoc />
    public override void InitializeViewModel(ISettingViewModel vm, string? savedValue)
    {
        vm.StringValue = savedValue ?? DefaultValue;

        vm.ChoicesValue = Choices;
    }

    /// <inheritdoc />
    public override string GetValueFromViewModel(ISettingViewModel vm)
    {
        return vm.StringValue;
    }
}