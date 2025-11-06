namespace Axorith.Sdk.Settings;

/// <summary>
///     Defines how a setting participates in persistence.
/// </summary>
public enum SettingPersistence
{
    /// <summary>
    ///     Setting value is serialized into presets (default).
    /// </summary>
    Persisted,

    /// <summary>
    ///     Setting value is not serialized and only lives during the editing/viewing session.
    /// </summary>
    Ephemeral,

    /// <summary>
    ///     Transient trigger-like setting. Never persisted and typically used only to signal an action.
    /// </summary>
    Transient
}