using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Contracts;
using Axorith.Core.Models;
using Grpc.Core;
using Polly.Retry;
using ConfiguredModule = Axorith.Contracts.ConfiguredModule;
using PresetSummary = Axorith.Client.CoreSdk.Abstractions.PresetSummary;

namespace Axorith.Client.CoreSdk;

/// <summary>
///     gRPC implementation of IPresetsApi.
/// </summary>
internal class GrpcPresetsApi(PresetsService.PresetsServiceClient client, AsyncRetryPolicy retryPolicy)
    : IPresetsApi
{
    public async Task<IReadOnlyList<PresetSummary>> ListPresetsAsync(CancellationToken ct = default)
    {
        return await retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.ListPresetsAsync(new ListPresetsRequest(), cancellationToken: ct)
                .ConfigureAwait(false);

            return response.Presets
                .Select(p => new PresetSummary(
                    Guid.Parse(p.Id),
                    p.Name,
                    p.Version,
                    p.ModuleCount))
                .ToList();
        }).ConfigureAwait(false);
    }

    public async Task<SessionPreset?> GetPresetAsync(Guid presetId, CancellationToken ct = default)
    {
        return await retryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                var response = await client.GetPresetAsync(
                        new GetPresetRequest { PresetId = presetId.ToString() },
                        cancellationToken: ct)
                    .ConfigureAwait(false);

                return ToModel(response);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                return null;
            }
        }).ConfigureAwait(false);
    }

    public async Task<SessionPreset> CreatePresetAsync(SessionPreset preset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preset);

        return await retryPolicy.ExecuteAsync(async () =>
        {
            var message = ToMessage(preset);
            var response = await client.CreatePresetAsync(
                    new CreatePresetRequest { Preset = message },
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return ToModel(response);
        }).ConfigureAwait(false);
    }

    public async Task<SessionPreset> UpdatePresetAsync(SessionPreset preset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preset);

        return await retryPolicy.ExecuteAsync(async () =>
        {
            var message = ToMessage(preset);
            var response = await client.UpdatePresetAsync(
                    new UpdatePresetRequest { Preset = message },
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return ToModel(response);
        }).ConfigureAwait(false);
    }

    public async Task DeletePresetAsync(Guid presetId, CancellationToken ct = default)
    {
        await retryPolicy.ExecuteAsync(async () =>
        {
            await client.DeletePresetAsync(
                    new DeletePresetRequest { PresetId = presetId.ToString() },
                    cancellationToken: ct)
                .ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private static Preset ToMessage(SessionPreset preset)
    {
        var message = new Preset
        {
            Id = preset.Id.ToString(),
            Name = preset.Name,
            Version = preset.Version,
            FocusCommitment = ToMessage(preset.FocusCommitment)
        };

        foreach (var module in preset.Modules)
        {
            message.Modules.Add(new ConfiguredModule
            {
                InstanceId = module.InstanceId.ToString(),
                ModuleId = module.ModuleId.ToString(),
                CustomName = module.CustomName ?? string.Empty,
                StartDelaySeconds = module.StartDelay.TotalSeconds,
                Settings = { module.Settings }
            });
        }

        return message;
    }

    private static SessionPreset ToModel(Preset message)
    {
        return new SessionPreset
        {
            Id = Guid.TryParse(message.Id, out var id) ? id : Guid.NewGuid(),
            Name = message.Name,
            Version = message.Version,
            FocusCommitment = ToModel(message.FocusCommitment),
            Modules =
            [
                .. message.Modules.Select(m => new Core.Models.ConfiguredModule
                {
                    InstanceId = Guid.TryParse(m.InstanceId, out var iid) ? iid : Guid.NewGuid(),
                    ModuleId = Guid.Parse(m.ModuleId),
                    CustomName = string.IsNullOrWhiteSpace(m.CustomName) ? null : m.CustomName,
                    StartDelay = TimeSpan.FromSeconds(m.StartDelaySeconds),
                    Settings = new Dictionary<string, string>(m.Settings)
                })
            ]
        };
    }

    private static Axorith.Contracts.FocusCommitmentOptions ToMessage(Core.Models.FocusCommitmentOptions options) => new()
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

    private static Core.Models.FocusCommitmentOptions ToModel(Axorith.Contracts.FocusCommitmentOptions options)
    {
        var result = new Core.Models.FocusCommitmentOptions
        {
            Mode = (Core.Models.FocusCommitmentMode)options.Mode,
            EndCondition = (Core.Models.FocusEndCondition)options.EndCondition,
            Duration = options.DurationSeconds > 0 ? TimeSpan.FromSeconds(options.DurationSeconds) : null,
            EndAtLocalTime = options is { HasEndAtLocalTime: true, EndAtHour: >= 0 and <= 23, EndAtMinute: >= 0 and <= 59 }
                ? new TimeOnly(options.EndAtHour, options.EndAtMinute)
                : null,
            EndAtDaysOfWeek = [.. options.EndAtDaysOfWeek.Select(day => (DayOfWeek)day)],
            BreakCount = Math.Clamp(options.BreakCount, 0, 20),
            BreakDuration = options.BreakDurationSeconds > 0
                ? TimeSpan.FromSeconds(options.BreakDurationSeconds)
                : TimeSpan.FromMinutes(5),
            AfterEnd = (Core.Models.AfterEndBehavior)options.AfterEnd,
            ScheduleLockMinutes = Math.Clamp(options.ScheduleLockMinutes, 0, 60)
        };

        if (Guid.TryParse(options.NextWorkspaceId, out var nextWorkspaceId))
        {
            result.NextWorkspaceId = nextWorkspaceId;
        }

        return result;
    }
}
