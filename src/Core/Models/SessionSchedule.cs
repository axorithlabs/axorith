namespace Axorith.Core.Models;

public enum ScheduleType
{
    OneTime,
    Recurring,
    StopRecurring,
    StopDuration
}

public class SessionSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PresetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public ScheduleType Type { get; set; }
    public DateTimeOffset? OneTimeDate { get; set; }
    public TimeSpan? RecurringTime { get; set; }
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];
    public DateTimeOffset? LastRun { get; set; }

    /// <summary>
    ///     Duration after which the session should automatically stop.
    ///     Null means no auto-stop.
    /// </summary>
    public TimeSpan? AutoStopDuration { get; set; }

    /// <summary>
    ///     ID of the preset to automatically start after the current session ends.
    ///     Null means just stop the session without starting another one.
    /// </summary>
    public Guid? NextPresetId { get; set; }

    /// <summary>
    ///     Whether to display time in 24-hour format (true) or 12-hour AM/PM format (false).
    /// </summary>
    public bool Use24HourFormat { get; set; } = true;

    public DateTimeOffset? GetNextRun(DateTimeOffset now)
    {
        if (!IsEnabled)
        {
            return null;
        }

        if (Type == ScheduleType.OneTime)
        {
            if (!OneTimeDate.HasValue)
            {
                return null;
            }

            var diff = OneTimeDate.Value - now;
            return diff.TotalSeconds >= -30 ? OneTimeDate : null;
        }

        if ((Type != ScheduleType.Recurring && Type != ScheduleType.StopRecurring) || !RecurringTime.HasValue)
        {
            return null;
        }

        var localNow = now.LocalDateTime;
        TimeSpan.FromSeconds(30);

        for (var i = 0; i <= 7; i++)
        {
            var candidateDate = localNow.Date.AddDays(i);
            var candidateRun = candidateDate + RecurringTime.Value;

            if (i == 0 && candidateRun < localNow)
            {
                continue;
            }

            if (DaysOfWeek.Count > 0 && !DaysOfWeek.Contains(candidateDate.DayOfWeek))
            {
                continue;
            }

            return new DateTimeOffset(candidateRun, TimeZoneInfo.Local.GetUtcOffset(candidateRun));
        }

        return null;
    }
}