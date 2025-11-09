using Axorith.Sdk;
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
            message.Platforms.Add(platform.ToString());

        return message;
    }

    public static Sdk.ModuleDefinition ToModel(ModuleDefinition message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!Guid.TryParse(message.Id, out var id))
            throw new ArgumentException($"Invalid module Id: {message.Id}", nameof(message));

        var platforms = message.Platforms
            .Select(p => Enum.TryParse<Platform>(p, out var platform) ? platform : Platform.Windows)
            .ToArray();

        return new Sdk.ModuleDefinition
        {
            Id = id,
            Name = message.Name,
            Description = message.Description,
            Category = message.Category,
            Platforms = platforms,
            AssemblyFileName = message.Assembly
        };
    }
}