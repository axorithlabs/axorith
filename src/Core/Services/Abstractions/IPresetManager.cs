using Axorith.Core.Models;

namespace Axorith.Core.Services.Abstractions;

public interface IPresetManager
{
    Task<IReadOnlyList<SessionPreset>> LoadAllPresetsAsync(CancellationToken cancellationToken);

    Task<SessionPreset?> GetPresetByIdAsync(Guid presetId, CancellationToken cancellationToken);

    Task SavePresetAsync(SessionPreset preset, CancellationToken cancellationToken);

    Task DeletePresetAsync(Guid presetId, CancellationToken cancellationToken);
}
