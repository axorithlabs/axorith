namespace Axorith.Core.Models;

/// <summary>
/// Represents a single module that has been configured by the user within a session preset.
/// </summary>
public class ConfiguredModule
{
    /// <summary>
    /// The unique identifier of the module definition. This links back to the IModule in the registry.
    /// </summary>
    public Guid ModuleId { get; set; }

    /// <summary>
    /// A dictionary containing the user-provided settings for this specific module instance.
    /// The key is the setting's key (from ModuleSetting.Key), and the value is the user's input.
    /// </summary>
    public Dictionary<string, string> Settings { get; set; } = new();
}