using Axorith.Core.Models;
using ContractConfiguredModule = Axorith.Contracts.ConfiguredModule;
using ContractFocusCommitmentOptions = Axorith.Contracts.FocusCommitmentOptions;
using CoreConfiguredModule = Axorith.Core.Models.ConfiguredModule;
using CoreFocusCommitmentOptions = Axorith.Core.Models.FocusCommitmentOptions;
using CoreSessionPreset = Axorith.Core.Models.SessionPreset;

namespace Axorith.Contracts;

/// <summary>Converts presets between the domain model and gRPC messages.</summary>
public static class PresetCodec
{
    /// <summary>Creates a gRPC message from a domain preset.</summary>
    /// <param name="preset">The preset to convert.</param>
    /// <returns>The corresponding gRPC message.</returns>
    public static Preset ToMessage(CoreSessionPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var message = new Preset
        {
            Id = preset.Id.ToString(),
            Name = preset.Name,
            Version = preset.Version,
            FocusCommitment = ToMessage(preset.FocusCommitment)
        };
        message.Modules.AddRange(preset.Modules.Select(ToMessage));
        return message;
    }


    /// <summary>Creates a summary message from a domain preset.</summary>
    /// <param name="preset">The preset to summarize.</param>
    /// <returns>The corresponding summary message.</returns>
    public static PresetSummary ToSummary(CoreSessionPreset preset)
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

    /// <summary>Creates a domain preset from a gRPC message.</summary>
    /// <param name="message">The message to convert.</param>
    /// <returns>The corresponding domain preset.</returns>
    public static CoreSessionPreset ToModel(Preset message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new SessionPreset
        {
            Id = Guid.TryParse(message.Id, out var id) ? id : Guid.NewGuid(),
            Name = message.Name,
            Version = message.Version,
            FocusCommitment = message.FocusCommitment is null ? new() : ToModel(message.FocusCommitment),
            Modules = [.. message.Modules.Select(ToModel)]
        };
    }

    private static CoreFocusCommitmentOptions ToModel(ContractFocusCommitmentOptions options)
    {
        var result = new CoreFocusCommitmentOptions
        {
            Mode = (Axorith.Core.Models.FocusCommitmentMode)options.Mode,
            EndCondition = (Axorith.Core.Models.FocusEndCondition)options.EndCondition,
            Duration = options.DurationSeconds > 0 ? TimeSpan.FromSeconds(options.DurationSeconds) : null,
            EndAtLocalTime = options is { HasEndAtLocalTime: true, EndAtHour: >= 0 and <= 23, EndAtMinute: >= 0 and <= 59 }
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
            result.NextWorkspaceId = nextWorkspaceId;
        return result;
    }

    private static ContractFocusCommitmentOptions ToMessage(CoreFocusCommitmentOptions options) => new()
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
        ScheduleLockMinutes = options.ScheduleLockMinutes,
        EndAtDaysOfWeek = { options.EndAtDaysOfWeek.Select(day => (int)day) }
    };

    private static ContractConfiguredModule ToMessage(CoreConfiguredModule module) => new()
    {
        InstanceId = module.InstanceId.ToString(),
        ModuleId = module.ModuleId.ToString(),
        CustomName = module.CustomName ?? string.Empty,
        StartDelaySeconds = module.StartDelay.TotalSeconds,
        Settings = { module.Settings }
    };

    private static CoreConfiguredModule ToModel(ContractConfiguredModule message)
    {
        if (!Guid.TryParse(message.ModuleId, out var moduleId))
            throw new ArgumentException($"Invalid ModuleId: {message.ModuleId}", nameof(message));

        return new CoreConfiguredModule
        {
            InstanceId = Guid.TryParse(message.InstanceId, out var instanceId) ? instanceId : Guid.NewGuid(),
            ModuleId = moduleId,
            CustomName = string.IsNullOrWhiteSpace(message.CustomName) ? null : message.CustomName,
            StartDelay = TimeSpan.FromSeconds(message.StartDelaySeconds),
            Settings = new Dictionary<string, string>(message.Settings)
        };
    }
}
