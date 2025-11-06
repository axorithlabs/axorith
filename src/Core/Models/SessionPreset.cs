namespace Axorith.Core.Models;

/// <summary>
///     Represents a complete, user-defined session preset.
///     This is the main data object that gets serialized to and from storage (e.g., a JSON file).
/// </summary>
public class SessionPreset
{
    /// <summary>
    ///     Schema version for preset format. Used for migration when structure changes.
    ///     Current version: 1
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    ///     A unique identifier for this preset. Crucial for updating and deleting.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    ///     The user-friendly name of the preset, e.g., "Morning Coding Focus".
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     The list of modules that are part of this session, along with their specific configurations.
    /// </summary>
    public List<ConfiguredModule> Modules { get; set; } = new();
}