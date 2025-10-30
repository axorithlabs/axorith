using System.Runtime.Loader;
using System.Text.Json.Serialization;

namespace Axorith.Sdk;

/// <summary>
///     Represents the static, metadata-based definition of a module, deserialized from a 'module.json' file.
///     This class is an immutable data record that serves as a module's "passport".
/// </summary>
public record ModuleDefinition
{
    /// <summary>
    ///     Gets the unique identifier for the module.
    ///     This is mapped from the "id" field in module.json.
    /// </summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>
    ///     Gets the user-friendly name of the module.
    ///     This is mapped from the "name" field in module.json.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    ///     Gets a detailed description of what the module does.
    ///     This is mapped from the "description" field in module.json.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the category for grouping the module in the UI.
    ///     This is mapped from the "category" field in module.json.
    /// </summary>
    [JsonPropertyName("category")]
    public string Category { get; init; } = "General";

    /// <summary>
    ///     Gets the set of operating systems that this module supports.
    ///     This is mapped from the "platforms" array in module.json.
    /// </summary>
    [JsonPropertyName("platforms")]
    public Platform[] Platforms { get; init; } = [];

    /// <summary>
    ///     Gets or sets the actual <see cref="System.Type" /> of the class implementing <see cref="IModule" />.
    ///     This property is resolved by the ModuleLoader at runtime and is not part of the JSON file.
    /// </summary>
    [JsonIgnore]
    public Type? ModuleType { get; set; }

    /// <summary>
    ///     Gets or sets the AssemblyLoadContext used to load this module.
    ///     This is used to unload the module's assemblies from memory.
    /// </summary>
    [JsonIgnore]
    public AssemblyLoadContext? LoadContext { get; set; }
}