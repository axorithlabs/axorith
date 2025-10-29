namespace Axorith.Sdk.Settings;

/// <summary>
/// Defines a contract for a ViewModel that can store setting values of different types.
/// This acts as a bridge (Visitor pattern) between the strongly-typed SettingBase classes in the SDK
/// and the UI-specific ViewModel in the Client, eliminating the need for casting.
/// </summary>
public interface ISettingViewModel
{
    /// <summary>
    /// Gets or sets the value for a setting that is represented as a string (e.g., TextSetting).
    /// </summary>
    string StringValue { get; set; }
    
    /// <summary>
    /// Gets or sets the value for a setting that is represented as a boolean (e.g., CheckboxSetting).
    /// </summary>
    bool BoolValue { get; set; }
    
    /// <summary>
    /// Gets or sets the value for a setting that is represented as a decimal (e.g., NumberSetting).
    /// </summary>
    decimal DecimalValue { get; set; }
    
    /// <summary>
    /// Gets or sets the value for a setting that is represented as a choices (e.g., ChoiceSetting).
    /// </summary>
    IReadOnlyList<KeyValuePair<string, string>> ChoicesValue { get; set; }
}