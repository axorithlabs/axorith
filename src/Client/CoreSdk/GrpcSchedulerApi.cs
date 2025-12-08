using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Contracts;
using Axorith.Core.Models;
using Google.Protobuf.WellKnownTypes;
using Polly.Retry;

namespace Axorith.Client.CoreSdk;

internal class GrpcSchedulerApi(SchedulerService.SchedulerServiceClient client, AsyncRetryPolicy retryPolicy)
    : ISchedulerApi
{
    public async Task<IReadOnlyList<SessionSchedule>> ListSchedulesAsync(CancellationToken ct = default)
    {
        return await retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.ListSchedulesAsync(new ListSchedulesRequest(), cancellationToken: ct)
                .ConfigureAwait(false);

            return response.Schedules.Select(ToModel).ToList();
        }).ConfigureAwait(false);
    }

    public async Task<SessionSchedule> CreateScheduleAsync(SessionSchedule schedule, CancellationToken ct = default)
    {
        return await retryPolicy.ExecuteAsync(async () =>
        {
            var msg = ToMessage(schedule);
            var response = await client
                .CreateScheduleAsync(new CreateScheduleRequest { Schedule = msg }, cancellationToken: ct)
                .ConfigureAwait(false);
            return ToModel(response);
        }).ConfigureAwait(false);
    }

    public async Task<SessionSchedule> UpdateScheduleAsync(SessionSchedule schedule, CancellationToken ct = default)
    {
        return await retryPolicy.ExecuteAsync(async () =>
        {
            var msg = ToMessage(schedule);
            var response = await client
                .UpdateScheduleAsync(new UpdateScheduleRequest { Schedule = msg }, cancellationToken: ct)
                .ConfigureAwait(false);
            return ToModel(response);
        }).ConfigureAwait(false);
    }

    public async Task DeleteScheduleAsync(Guid scheduleId, CancellationToken ct = default)
    {
        await retryPolicy.ExecuteAsync(async () =>
        {
            await client.DeleteScheduleAsync(new DeleteScheduleRequest { ScheduleId = scheduleId.ToString() },
                    cancellationToken: ct)
                .ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task<SessionSchedule> SetEnabledAsync(Guid scheduleId, bool enabled, CancellationToken ct = default)
    {
        return await retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.SetEnabledAsync(new SetScheduleEnabledRequest
            {
                ScheduleId = scheduleId.ToString(),
                Enabled = enabled
            }, cancellationToken: ct).ConfigureAwait(false);

            return ToModel(response);
        }).ConfigureAwait(false);
    }

    private static Schedule ToMessage(SessionSchedule model)
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

        msg.DaysOfWeek.AddRange(model.DaysOfWeek.Select(d => (int)d));

        if (model.AutoStopDuration.HasValue && model.AutoStopDuration.Value > TimeSpan.Zero)
        {
            msg.AutoStopDurationSeconds = (long)model.AutoStopDuration.Value.TotalSeconds;
        }

        if (model.NextPresetId.HasValue)
        {
            msg.NextPresetId = model.NextPresetId.Value.ToString();
        }

        return msg;
    }

    private static SessionSchedule ToModel(Schedule message)
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

        if (message.LastRun != null)
        {
            model.LastRun = message.LastRun.ToDateTimeOffset();
        }

        if (message.AutoStopDurationSeconds > 0)
        {
            model.AutoStopDuration = TimeSpan.FromSeconds(message.AutoStopDurationSeconds);
        }

        if (!string.IsNullOrWhiteSpace(message.NextPresetId) &&
            Guid.TryParse(message.NextPresetId, out var nextPresetId))
        {
            model.NextPresetId = nextPresetId;
        }

        return model;
    }
}