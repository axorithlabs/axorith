using Axorith.Core.Models;
using Google.Protobuf.WellKnownTypes;

namespace Axorith.Contracts;

/// <summary>Converts schedules between the domain model and gRPC messages.</summary>
public static class ScheduleCodec
{
    /// <summary>Creates a gRPC message from a domain schedule.</summary>
    /// <param name="model">The schedule to convert.</param>
    /// <param name="includeNextRun">Whether to calculate and include the next run time.</param>
    /// <returns>The corresponding gRPC message.</returns>
    public static Schedule ToMessage(SessionSchedule model, bool includeNextRun = false)
    {
        var message = new Schedule
        {
            Id = model.Id.ToString(),
            PresetId = model.PresetId.ToString(),
            Name = model.Name,
            IsEnabled = model.IsEnabled,
            Type = (int)model.Type,
            RecurringTime = model.RecurringTime?.ToString(@"hh\:mm") ?? string.Empty,
            Use24HourFormat = model.Use24HourFormat,
            NextPresetId = model.NextPresetId?.ToString() ?? string.Empty
        };

        if (model.OneTimeDate is { } oneTimeDate)
            message.OneTimeDate = Timestamp.FromDateTimeOffset(oneTimeDate);
        if (model.LastRun is { } lastRun)
            message.LastRun = Timestamp.FromDateTimeOffset(lastRun);
        if (model.AutoStopDuration is { } duration && duration > TimeSpan.Zero)
            message.AutoStopDurationSeconds = (long)duration.TotalSeconds;
        if (includeNextRun && model.GetNextRun(DateTimeOffset.UtcNow) is { } nextRun)
            message.NextRun = Timestamp.FromDateTimeOffset(nextRun);

        message.DaysOfWeek.AddRange(model.DaysOfWeek.Select(day => (int)day));
        return message;
    }

    /// <summary>Creates a domain schedule from a gRPC message.</summary>
    /// <param name="message">The message to convert.</param>
    /// <param name="includeLastRun">Whether to retain the message's last run time.</param>
    /// <returns>The corresponding domain schedule.</returns>
    public static SessionSchedule ToModel(Schedule message, bool includeLastRun = false)
    {
        var model = new SessionSchedule
        {
            Id = Guid.TryParse(message.Id, out var id) ? id : Guid.NewGuid(),
            PresetId = Guid.TryParse(message.PresetId, out var presetId) ? presetId : Guid.Empty,
            Name = message.Name,
            IsEnabled = message.IsEnabled,
            Type = (ScheduleType)message.Type,
            Use24HourFormat = message.Use24HourFormat,
            OneTimeDate = message.OneTimeDate?.ToDateTimeOffset(),
            DaysOfWeek = [.. message.DaysOfWeek.Select(day => (DayOfWeek)day)],
            AutoStopDuration = message.AutoStopDurationSeconds > 0
                ? TimeSpan.FromSeconds(message.AutoStopDurationSeconds)
                : null
        };

        if (TimeSpan.TryParse(message.RecurringTime, out var recurringTime))
            model.RecurringTime = recurringTime;
        if (includeLastRun && message.LastRun is not null)
            model.LastRun = message.LastRun.ToDateTimeOffset();
        if (Guid.TryParse(message.NextPresetId, out var nextPresetId))
            model.NextPresetId = nextPresetId;

        return model;
    }
}
