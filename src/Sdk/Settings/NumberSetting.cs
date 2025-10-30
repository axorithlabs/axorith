using System.Globalization;

namespace Axorith.Sdk.Settings;

/// <summary>
///     Represents a module setting that accepts a decimal number,
///     typically rendered as a NumericUpDown control in the UI.
/// </summary>
public class NumberSetting : SettingBase
{
    /// <summary>
    ///     Gets the default value for this setting as a decimal.
    /// </summary>
    public decimal DefaultValue { get; }

    /// <summary>
    ///     Gets the default value for this setting, boxed as an object.
    /// </summary>
    public override object DefaultValueObject => DefaultValue;

    /// <summary>
    ///     Initializes a new instance of the <see cref="NumberSetting" /> class.
    /// </summary>
    /// <param name="key">The unique, machine-readable key for the setting.</param>
    /// <param name="label">The human-readable name displayed in the UI.</param>
    /// <param name="description">An optional description for a tooltip.</param>
    /// <param name="defaultValue">The default numeric value for this setting.</param>
    public NumberSetting(string key, string label, string? description = null, decimal defaultValue = 0)
        : base(key, label, description)
    {
        DefaultValue = defaultValue;
    }

    /// <summary>
    ///     Parses the saved string value into a decimal and sets the <see cref="ISettingViewModel.DecimalValue" /> property on
    ///     the provided ViewModel.
    ///     If parsing fails or the saved value is null, the default value is used.
    /// </summary>
    /// <param name="vm">The ViewModel to initialize.</param>
    /// <param name="savedValue">The raw string value loaded from storage.</param>
    public override void InitializeViewModel(ISettingViewModel vm, string? savedValue)
    {
        vm.DecimalValue = decimal.TryParse(savedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? d
            : DefaultValue;
    }

    /// <summary>
    ///     Retrieves the current decimal value from the <see cref="ISettingViewModel.DecimalValue" /> property
    ///     and formats it as a culture-invariant string for serialization.
    /// </summary>
    /// <param name="vm">The ViewModel containing the current user-provided value.</param>
    /// <returns>A string representation of the current value, suitable for saving in JSON.</returns>
    public override string GetValueFromViewModel(ISettingViewModel vm)
    {
        return vm.DecimalValue.ToString(CultureInfo.InvariantCulture);
    }
}