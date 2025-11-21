namespace Axorith.Core.Models;

/// <summary>
///     Represents a single instance of a module configured by the user within a session preset.
/// </summary>
public class ConfiguredModule
{
    /// <summary>
    ///     A unique identifier for THIS SPECIFIC INSTANCE of the module within the preset.
    ///     This is generated when the module is added to the preset.
    /// </summary>
    public Guid InstanceId { get; set; } = Guid.NewGuid();

    /// <summary>
    ///     The unique identifier of the module's definition. This links back to the ModuleDefinition in the registry.
    /// </summary>
    public Guid ModuleId { get; set; }

    /// <summary>
    ///     An optional, user-defined name for this instance (e.g., "Launch Notepad", "Launch OBS").
    ///     If null or empty, the module's default name will be used.
    /// </summary>
    public string? CustomName { get; set; }

    /// <summary>
    ///     Delay before starting this module in the sequential pipeline.
    ///     The session manager will wait this amount of time AFTER the previous module starts
    ///     and BEFORE this module starts.
    /// </summary>
    public TimeSpan StartDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     A dictionary containing the user-provided settings for this specific module instance.
    /// </summary>
    public Dictionary<string, string> Settings { get; set; } = [];
}