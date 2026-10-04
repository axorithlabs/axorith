using Axorith.Core.Models;

namespace Axorith.Client.CoreSdk.Abstractions;

public interface ISchedulerApi
{
    Task<IReadOnlyList<SessionSchedule>> ListSchedulesAsync(CancellationToken ct = default);

    Task<ConfigurationLockStatus> GetConfigurationLockStatusAsync(Guid presetId, CancellationToken ct = default);

    Task<SessionSchedule> CreateScheduleAsync(SessionSchedule schedule, CancellationToken ct = default);

    Task<SessionSchedule> UpdateScheduleAsync(SessionSchedule schedule, CancellationToken ct = default);

    Task DeleteScheduleAsync(Guid scheduleId, CancellationToken ct = default);

    Task<SessionSchedule> SetEnabledAsync(Guid scheduleId, bool enabled, CancellationToken ct = default);
}
