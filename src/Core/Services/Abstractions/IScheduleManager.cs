using Axorith.Core.Models;

namespace Axorith.Core.Services.Abstractions;

public interface IScheduleManager : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);

    Task StartProcessingAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionSchedule>> ListSchedulesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionSchedule>> GetSchedulesForPresetAsync(Guid presetId, CancellationToken cancellationToken);

    Task<SessionSchedule> SaveScheduleAsync(SessionSchedule schedule, CancellationToken cancellationToken);

    Task DeleteScheduleAsync(Guid scheduleId, CancellationToken cancellationToken);

    Task<SessionSchedule?> SetEnabledAsync(Guid scheduleId, bool enabled, CancellationToken cancellationToken);

    Task<ConfigurationLockStatus> GetConfigurationLockStatusAsync(Guid presetId,
        CancellationToken cancellationToken);
}
