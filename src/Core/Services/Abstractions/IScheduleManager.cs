using Axorith.Core.Models;

namespace Axorith.Core.Services.Abstractions;

/// <summary>
///     Manages session schedules and triggers automated session starts.
/// </summary>
public interface IScheduleManager : IAsyncDisposable
{
    /// <summary>
    ///     Starts the background scheduler loop.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Lists all configured schedules.
    /// </summary>
    Task<IReadOnlyList<SessionSchedule>> ListSchedulesAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Creates or updates a schedule.
    /// </summary>
    Task<SessionSchedule> SaveScheduleAsync(SessionSchedule schedule, CancellationToken cancellationToken);

    /// <summary>
    ///     Deletes a schedule by ID.
    /// </summary>
    Task DeleteScheduleAsync(Guid scheduleId, CancellationToken cancellationToken);

    /// <summary>
    ///     Enables or disables a schedule.
    /// </summary>
    Task<SessionSchedule?> SetEnabledAsync(Guid scheduleId, bool enabled, CancellationToken cancellationToken);
}