using Axorith.Core.Models;

namespace Axorith.Client.CoreSdk.Abstractions;

/// <summary>
///     API for managing session schedules.
/// </summary>
public interface ISchedulerApi
{
    /// <summary>
    ///     Lists all configured schedules.
    /// </summary>
    Task<IReadOnlyList<SessionSchedule>> ListSchedulesAsync(CancellationToken ct = default);

    /// <summary>
    ///     Creates a new schedule.
    /// </summary>
    Task<SessionSchedule> CreateScheduleAsync(SessionSchedule schedule, CancellationToken ct = default);

    /// <summary>
    ///     Updates an existing schedule.
    /// </summary>
    Task<SessionSchedule> UpdateScheduleAsync(SessionSchedule schedule, CancellationToken ct = default);

    /// <summary>
    ///     Deletes a schedule by ID.
    /// </summary>
    Task DeleteScheduleAsync(Guid scheduleId, CancellationToken ct = default);

    /// <summary>
    ///     Enables or disables a schedule.
    /// </summary>
    Task<SessionSchedule> SetEnabledAsync(Guid scheduleId, bool enabled, CancellationToken ct = default);
}