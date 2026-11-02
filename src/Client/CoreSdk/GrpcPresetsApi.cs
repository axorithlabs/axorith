using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Contracts;
using Axorith.Core.Models;
using Grpc.Core;
using Polly.Retry;
using PresetSummary = Axorith.Client.CoreSdk.Abstractions.PresetSummary;

namespace Axorith.Client.CoreSdk;

internal class GrpcPresetsApi(PresetsService.PresetsServiceClient client, AsyncRetryPolicy retryPolicy)
    : IPresetsApi
{
    public Task<IReadOnlyList<PresetSummary>> ListPresetsAsync(CancellationToken ct = default) =>
        GrpcResultMapper.ExecuteAsync<ListPresetsResponse, IReadOnlyList<PresetSummary>>(retryPolicy,
            () => client.ListPresetsAsync(new ListPresetsRequest(), cancellationToken: ct),
            response => [.. response.Presets.Select(p => new PresetSummary(Guid.Parse(p.Id), p.Name, p.Version, p.ModuleCount))]);

    public Task<SessionPreset?> GetPresetAsync(Guid presetId, CancellationToken ct = default) =>
        retryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                var response = await client.GetPresetAsync(
                        new GetPresetRequest { PresetId = presetId.ToString() }, cancellationToken: ct)
                    .ConfigureAwait(false);
                return PresetCodec.ToModel(response);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                return null;
            }
        });

    public Task<SessionPreset> CreatePresetAsync(SessionPreset preset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return GrpcResultMapper.ExecuteAsync(retryPolicy, () => client.CreatePresetAsync(
            new CreatePresetRequest { Preset = PresetCodec.ToMessage(preset) }, cancellationToken: ct), PresetCodec.ToModel);
    }

    public Task<SessionPreset> UpdatePresetAsync(SessionPreset preset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return GrpcResultMapper.ExecuteAsync(retryPolicy, () => client.UpdatePresetAsync(
            new UpdatePresetRequest { Preset = PresetCodec.ToMessage(preset) }, cancellationToken: ct), PresetCodec.ToModel);
    }

    public Task DeletePresetAsync(Guid presetId, CancellationToken ct = default) =>
        GrpcResultMapper.ExecuteAsync(retryPolicy, () => client.DeletePresetAsync(
            new DeletePresetRequest { PresetId = presetId.ToString() }, cancellationToken: ct), response => response);
}
