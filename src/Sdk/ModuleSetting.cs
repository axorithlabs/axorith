namespace Axorith.Sdk;

/// <summary>
/// Describes a single configurable option for a module.
/// This object is immutable after creation.
/// </summary>
/// <example>
/// <code>
/// var setting = new ModuleSetting(
///     key: "enableSuperFeature",
///     label: "Enable Super Feature",
///     type: SettingType.Checkbox,
///     defaultValue: "true",
///     description: "This setting toggles the experimental super feature."
/// );
/// </code>
/// </example>
public class ModuleSetting
{
    /// <summary>
    /// The unique key for this setting within the module. Used for saving and retrieving the value.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// The human-readable name for the setting, displayed in the UI.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// The type of UI control that should be rendered for this setting.
    /// </summary>
    public SettingType Type { get; }

    /// <summary>
    /// The default value for the setting, represented as a string. Guaranteed to not be null.
    /// </summary>
    public string DefaultValue { get; }

    /// <summary>
    /// An optional, more detailed description for the setting, often used as a tooltip in the UI.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Creates a new instance of a module setting definition.
    /// </summary>
    /// <param name="key">The unique key for the setting (e.g., "playlistUrl"). Cannot be null or empty.</param>
    /// <param name="label">The display name for the UI (e.g., "Playlist URL"). Cannot be null or empty.</param>
    /// <param name="type">The type of UI control to use.</param>
    /// <param name="defaultValue">The default value, as a string. Defaults to an empty string if not provided.</param>
    /// <param name="description">An optional description for a tooltip.</param>
    public ModuleSetting(string key, string label, SettingType type, string defaultValue = "", string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        Key = key;
        Label = label;
        Type = type;
        DefaultValue = defaultValue;
        Description = description;
    }
}