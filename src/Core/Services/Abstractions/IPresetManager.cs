using Axorith.Core.Models;

namespace Axorith.Core.Services.Abstractions;

/// <summary>
///     Defines a service for managing session presets (loading, saving, deleting).
/// </summary>
public interface IPresetManager
{
    /// <summary>
    ///     Loads all session presets from the persistent storage.
    /// </summary>
    Task<IReadOnlyList<SessionPreset>> LoadAllPresetsAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Loads a single preset by its ID from persistent storage.
    /// </summary>
    /// <param name="presetId">The ID of the preset to load.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The preset if found, otherwise null.</returns>
    Task<SessionPreset?> GetPresetByIdAsync(Guid presetId, CancellationToken cancellationToken);

    /// <summary>
    ///     Saves a session preset to the persistent storage.
    ///     If a preset with the same ID exists, it will be overwritten.
    /// </summary>
    Task SavePresetAsync(SessionPreset preset, CancellationToken cancellationToken);

    /// <summary>
    ///     Deletes a session preset from the persistent storage by its ID.
    /// </summary>
    Task DeletePresetAsync(Guid presetId, CancellationToken cancellationToken);
}