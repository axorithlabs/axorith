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
            Version = preset.Version,
            FocusCommitment = ToMessage(preset.FocusCommitment)
        };

        foreach (var module in preset.Modules)
        {
            message.Modules.Add(ToMessage(module));
        }

        return message;
    }

    public static SessionPreset ToModel(Preset message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!Guid.TryParse(message.Id, out var id))
        {
            id = Guid.NewGuid();
        }

        return new SessionPreset
        {
            Id = id,
            Name = message.Name,
            Version = message.Version,
            FocusCommitment = message.FocusCommitment == null
                ? new Axorith.Core.Models.FocusCommitmentOptions()
                : ToModel(message.FocusCommitment),
            Modules = [.. message.Modules.Select(ToModel)]
        };
    }

    private static Axorith.Core.Models.FocusCommitmentOptions ToModel(Axorith.Contracts.FocusCommitmentOptions options)
    {
        var result = new Axorith.Core.Models.FocusCommitmentOptions
        {
            Mode = (Axorith.Core.Models.FocusCommitmentMode)options.Mode,
            EndCondition = (Axorith.Core.Models.FocusEndCondition)options.EndCondition,
            Duration = options.DurationSeconds > 0 ? TimeSpan.FromSeconds(options.DurationSeconds) : null,
            EndAtLocalTime = options.HasEndAtLocalTime && options.EndAtHour is >= 0 and <= 23 &&
                             options.EndAtMinute is >= 0 and <= 59
                ? new TimeOnly(options.EndAtHour, options.EndAtMinute)
                : null,
            EndAtDaysOfWeek = [.. options.EndAtDaysOfWeek.Select(day => (DayOfWeek)day)],
            BreakCount = Math.Clamp(options.BreakCount, 0, 20),
            BreakDuration = options.BreakDurationSeconds > 0
                ? TimeSpan.FromSeconds(options.BreakDurationSeconds)
                : TimeSpan.FromMinutes(5),
            AfterEnd = (Axorith.Core.Models.AfterEndBehavior)options.AfterEnd,
            ScheduleLockMinutes = Math.Clamp(options.ScheduleLockMinutes, 0, 60)
        };

        if (Guid.TryParse(options.NextWorkspaceId, out var nextWorkspaceId))
        {
            result.NextWorkspaceId = nextWorkspaceId;
        }

        return result;
    }

    private static Axorith.Contracts.FocusCommitmentOptions ToMessage(Axorith.Core.Models.FocusCommitmentOptions options)
    {
        var message = new Axorith.Contracts.FocusCommitmentOptions
        {
            Mode = (Axorith.Contracts.FocusCommitmentMode)options.Mode,
            EndCondition = (Axorith.Contracts.FocusEndCondition)options.EndCondition,
            DurationSeconds = (long)(options.Duration?.TotalSeconds ?? 0),
            HasEndAtLocalTime = options.EndAtLocalTime.HasValue,
            EndAtHour = options.EndAtLocalTime?.Hour ?? 0,
            EndAtMinute = options.EndAtLocalTime?.Minute ?? 0,
            BreakCount = options.BreakCount,
            BreakDurationSeconds = (int)options.BreakDuration.TotalSeconds,
            AfterEnd = (Axorith.Contracts.AfterEndBehavior)options.AfterEnd,
            NextWorkspaceId = options.NextWorkspaceId?.ToString() ?? string.Empty,
            ScheduleLockMinutes = options.ScheduleLockMinutes
        };
        message.EndAtDaysOfWeek.AddRange(options.EndAtDaysOfWeek.Select(day => (int)day));

        return message;
    }

    public static ConfiguredModule ToMessage(Core.Models.ConfiguredModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var message = new ConfiguredModule
        {
            InstanceId = module.InstanceId.ToString(),
            ModuleId = module.ModuleId.ToString(),
            CustomName = module.CustomName ?? string.Empty,
            StartDelaySeconds = module.StartDelay.TotalSeconds // Map delay
        };

        foreach (var (key, value) in module.Settings)
        {
            message.Settings[key] = value;
        }

        return message;
    }

    public static Core.Models.ConfiguredModule ToModel(ConfiguredModule message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!Guid.TryParse(message.InstanceId, out var instanceId))
        {
            instanceId = Guid.NewGuid();
        }

        if (!Guid.TryParse(message.ModuleId, out var moduleId))
        {
            throw new ArgumentException($"Invalid ModuleId: {message.ModuleId}", nameof(message));
        }

        return new Core.Models.ConfiguredModule
        {
            InstanceId = instanceId,
            ModuleId = moduleId,
            CustomName = string.IsNullOrWhiteSpace(message.CustomName) ? null : message.CustomName,
            StartDelay = TimeSpan.FromSeconds(message.StartDelaySeconds),
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
