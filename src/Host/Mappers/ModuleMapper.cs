using ModuleDefinition = Axorith.Contracts.ModuleDefinition;

namespace Axorith.Host.Mappers;

/// <summary>
///     Maps between SDK ModuleDefinition and protobuf messages.
/// </summary>
public static class ModuleMapper
{
    public static ModuleDefinition ToMessage(Sdk.ModuleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var message = new ModuleDefinition
        {
            Id = definition.Id.ToString(),
            Name = definition.Name,
            Description = definition.Description,
            Category = definition.Category,
            Assembly = definition.AssemblyFileName ?? string.Empty
        };

        foreach (var platform in definition.Platforms)
        {
            message.Platforms.Add(platform.ToString());
        }

        return message;
    }
}