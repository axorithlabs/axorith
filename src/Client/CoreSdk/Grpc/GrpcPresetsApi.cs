using Axorith.Contracts;
using Axorith.Core.Models;
using Grpc.Core;
using Polly.Retry;
using ConfiguredModule = Axorith.Contracts.ConfiguredModule;

namespace Axorith.Client.CoreSdk.Grpc;

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
            Version = preset.Version
        };

        foreach (var module in preset.Modules)
            message.Modules.Add(new ConfiguredModule
            {
                InstanceId = module.InstanceId.ToString(),
                ModuleId = module.ModuleId.ToString(),
                CustomName = module.CustomName ?? string.Empty,
                Settings = { module.Settings }
            });

        return message;
    }

    private static SessionPreset ToModel(Preset message)
    {
        return new SessionPreset
        {
            Id = Guid.TryParse(message.Id, out var id) ? id : Guid.NewGuid(),
            Name = message.Name,
            Version = message.Version,
            Modules =
            [
                .. message.Modules.Select(m => new Core.Models.ConfiguredModule
                {
                    InstanceId = Guid.TryParse(m.InstanceId, out var iid) ? iid : Guid.NewGuid(),
                    ModuleId = Guid.Parse(m.ModuleId),
                    CustomName = string.IsNullOrWhiteSpace(m.CustomName) ? null : m.CustomName,
                    Settings = new Dictionary<string, string>(m.Settings)
                })
            ]
        };
    }
}