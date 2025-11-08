using Axorith.Core.Models;

namespace Axorith.Client.CoreSdk;

/// <summary>
///     API for preset management operations.
/// </summary>
public interface IPresetsApi
{
    /// <summary>
    ///     Lists all available presets (summary information).
    /// </summary>
    Task<IReadOnlyList<PresetSummary>> ListPresetsAsync(CancellationToken ct = default);

    /// <summary>
    ///     Gets full preset details including all configured modules.
    /// </summary>
    Task<SessionPreset?> GetPresetAsync(Guid presetId, CancellationToken ct = default);

    /// <summary>
    ///     Creates a new preset.
    ///     Returns the created preset with generated IDs.
    /// </summary>
    Task<SessionPreset> CreatePresetAsync(SessionPreset preset, CancellationToken ct = default);

    /// <summary>
    ///     Updates an existing preset.
    ///     Uses preset.Id to identify which preset to update.
    /// </summary>
    Task<SessionPreset> UpdatePresetAsync(SessionPreset preset, CancellationToken ct = default);

    /// <summary>
    ///     Deletes a preset by ID.
    /// </summary>
    Task DeletePresetAsync(Guid presetId, CancellationToken ct = default);
}

/// <summary>
///     Summary information for a preset (used in list views).
/// </summary>
public record PresetSummary(
    Guid Id,
    string Name,
    int Version,
    int ModuleCount
);