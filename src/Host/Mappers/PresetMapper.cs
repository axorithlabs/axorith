using Axorith.Contracts;
using Axorith.Core.Models;
using ConfiguredModule = Axorith.Contracts.ConfiguredModule;

namespace Axorith.Host.Mappers;

/// <summary>
///     Maps between Core SessionPreset models and protobuf Preset messages.
/// </summary>
public static class PresetMapper
{
    public static Preset ToMessage(SessionPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var message = new Preset
        {
            Id = preset.Id.ToString(),
            Name = preset.Name,
            Version = preset.Version
        };

        foreach (var module in preset.Modules) message.Modules.Add(ToMessage(module));

        return message;
    }

    public static SessionPreset ToModel(Preset message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!Guid.TryParse(message.Id, out var id)) id = Guid.NewGuid();

        return new SessionPreset
        {
            Id = id,
            Name = message.Name,
            Version = message.Version,
            Modules = [.. message.Modules.Select(ToModel)]
        };
    }

    public static ConfiguredModule ToMessage(Core.Models.ConfiguredModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var message = new ConfiguredModule
        {
            InstanceId = module.InstanceId.ToString(),
            ModuleId = module.ModuleId.ToString(),
            CustomName = module.CustomName ?? string.Empty
        };

        foreach (var (key, value) in module.Settings) message.Settings[key] = value;

        return message;
    }

    public static Core.Models.ConfiguredModule ToModel(ConfiguredModule message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!Guid.TryParse(message.InstanceId, out var instanceId)) instanceId = Guid.NewGuid();

        if (!Guid.TryParse(message.ModuleId, out var moduleId))
            throw new ArgumentException($"Invalid ModuleId: {message.ModuleId}", nameof(message));

        return new Core.Models.ConfiguredModule
        {
            InstanceId = instanceId,
            ModuleId = moduleId,
            CustomName = string.IsNullOrWhiteSpace(message.CustomName) ? null : message.CustomName,
            Settings = new Dictionary<string, string>(message.Settings)
        };
    }

    public static PresetSummary ToSummary(SessionPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        return new PresetSummary
        {
            Id = preset.Id.ToString(),
            Name = preset.Name,
            Version = preset.Version,
            ModuleCount = preset.Modules.Count
        };
    }
}