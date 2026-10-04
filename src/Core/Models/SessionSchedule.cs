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

    public TimeSpan? AutoStopDuration { get; set; }

    public Guid? NextPresetId { get; set; }

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
        var tolerance = TimeSpan.FromSeconds(30); // Allow 30 second window for triggering

        for (var i = 0; i <= 7; i++)
        {
            var candidateDate = localNow.Date.AddDays(i);
            var candidateRun = candidateDate + RecurringTime.Value;

            if (i == 0 && candidateRun < localNow.Add(-tolerance))
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

public sealed record ConfigurationLockStatus(bool IsLocked, TimeSpan? StartsIn);
