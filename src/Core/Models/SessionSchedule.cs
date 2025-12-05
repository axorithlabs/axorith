namespace Axorith.Core.Models;

public enum ScheduleType
{
    OneTime,
    Recurring
}

public class SessionSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PresetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public ScheduleType Type { get; set; }

    public DateTimeOffset? OneTimeDate { get; set; }

    public TimeSpan? RecurringTime { get; set; } // Time of day
    public List<DayOfWeek> DaysOfWeek { get; set; } = []; // Empty = every day

    public DateTimeOffset? LastRun { get; set; }

    /// <summary>
    /// Duration after which the session should automatically stop.
    /// Null means no auto-stop.
    /// </summary>
    public TimeSpan? AutoStopDuration { get; set; }

    /// <summary>
    /// ID of the preset to automatically start after the current session ends.
    /// Null means just stop the session without starting another one.
    /// </summary>
    public Guid? NextPresetId { get; set; }

    public DateTimeOffset? GetNextRun(DateTimeOffset now)
    {
        if (!IsEnabled)
        {
            return null;
        }

        if (Type == ScheduleType.OneTime)
        {
            return OneTimeDate > now ? OneTimeDate : null;
        }

        if (Type != ScheduleType.Recurring || !RecurringTime.HasValue)
        {
            return null;
        }
        
        var tolerance = TimeSpan.FromMinutes(5);

        for (var i = 0; i <= 7; i++)
        {
            var candidateDate = now.Date.AddDays(i);
            var candidateRun = candidateDate + RecurringTime.Value;

            if (i == 0 && candidateRun < now - tolerance)
            {
                continue;
            }

            // Check day of week filter
            if (DaysOfWeek.Count > 0 && !DaysOfWeek.Contains(candidateDate.DayOfWeek))
            {
                continue;
            }

            return candidateRun;
        }

        return null;
    }
}