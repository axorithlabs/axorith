using Axorith.Core.Models;

namespace Axorith.Client.CoreSdk.Abstractions;

public interface IPresetsApi
{
    Task<IReadOnlyList<PresetSummary>> ListPresetsAsync(CancellationToken ct = default);

    Task<SessionPreset?> GetPresetAsync(Guid presetId, CancellationToken ct = default);

    Task<SessionPreset> CreatePresetAsync(SessionPreset preset, CancellationToken ct = default);

    Task<SessionPreset> UpdatePresetAsync(SessionPreset preset, CancellationToken ct = default);

    Task DeletePresetAsync(Guid presetId, CancellationToken ct = default);
}

public record PresetSummary(
    Guid Id,
    string Name,
    int Version,
    int ModuleCount
);
