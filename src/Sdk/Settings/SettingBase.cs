namespace Axorith.Sdk.Settings;

/// <summary>
/// Defines the base contract for a single, configurable setting within a module.
/// Each derived class is responsible for its own type handling, default value, and UI interaction logic.
/// </summary>
public abstract class SettingBase
{
    /// <summary>
    /// The unique, machine-readable key for this setting (e.g., "apiKey").
    /// This key is used for serialization and retrieving the setting's value. It must not change between versions.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// The human-readable name for the setting, displayed in the UI (e.g., "API Key").
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// An optional, more detailed description for the setting, often used as a tooltip in the UI.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Gets the default value for this setting as a correctly typed object (e.g., bool, decimal, string).
    /// </summary>
    public abstract object DefaultValueObject { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingBase"/> class.
    /// </summary>
    /// <param name="key">The unique, machine-readable key for the setting.</param>
    /// <param name="label">The human-readable name displayed in the UI.</param>
    /// <param name="description">An optional description for a tooltip.</param>
    protected SettingBase(string key, string label, string? description)
    {
        Key = key;
        Label = label;
        Description = description;
    }

    /// <summary>
    /// Populates the provided ViewModel with the correct typed value, using either the saved value or the default.
    /// This is part of the Visitor pattern to avoid type casting in the ViewModel.
    /// </summary>
    /// <param name="vm">The ViewModel to initialize. It must implement <see cref="ISettingViewModel"/>.</param>
    /// <param name="savedValue">The raw string value loaded from storage, or null if not present.</param>
    public abstract void InitializeViewModel(ISettingViewModel vm, string? savedValue);

    /// <summary>
    /// Extracts the current value from the ViewModel and formats it into a string suitable for serialization.
    /// </summary>
    /// <param name="vm">The ViewModel containing the current user-provided value.</param>
    /// <returns>A string representation of the current value to be saved in JSON.</returns>
    public abstract string GetValueFromViewModel(ISettingViewModel vm);
}