using Axorith.Contracts;
using Axorith.Core.Models;
using Google.Protobuf.WellKnownTypes;

namespace Axorith.Host.Mappers;

public static class ScheduleMapper
{
    public static Schedule ToMessage(SessionSchedule model)
    {
        var msg = new Schedule
        {
            Id = model.Id.ToString(),
            PresetId = model.PresetId.ToString(),
            Name = model.Name,
            IsEnabled = model.IsEnabled,
            Type = (int)model.Type,
            RecurringTime = model.RecurringTime?.ToString(@"hh\:mm") ?? string.Empty
        };

        if (model.OneTimeDate.HasValue)
        {
            msg.OneTimeDate = Timestamp.FromDateTimeOffset(model.OneTimeDate.Value);
        }

        if (model.LastRun.HasValue)
        {
            msg.LastRun = Timestamp.FromDateTimeOffset(model.LastRun.Value);
        }

        // Calculate next run for display
        var nextRun = model.GetNextRun(DateTimeOffset.UtcNow);
        if (nextRun.HasValue)
        {
            msg.NextRun = Timestamp.FromDateTimeOffset(nextRun.Value);
        }

        msg.DaysOfWeek.AddRange(model.DaysOfWeek.Select(d => (int)d));

        return msg;
    }

    public static SessionSchedule ToModel(Schedule message)
    {
        var model = new SessionSchedule
        {
            Id = Guid.TryParse(message.Id, out var id) ? id : Guid.NewGuid(),
            PresetId = Guid.TryParse(message.PresetId, out var pid) ? pid : Guid.Empty,
            Name = message.Name,
            IsEnabled = message.IsEnabled,
            Type = (ScheduleType)message.Type
        };

        if (message.OneTimeDate != null)
        {
            model.OneTimeDate = message.OneTimeDate.ToDateTimeOffset();
        }

        if (!string.IsNullOrWhiteSpace(message.RecurringTime) && TimeSpan.TryParse(message.RecurringTime, out var ts))
        {
            model.RecurringTime = ts;
        }

        if (message.DaysOfWeek != null)
        {
            model.DaysOfWeek = message.DaysOfWeek.Select(d => (DayOfWeek)d).ToList();
        }

        return model;
    }
}