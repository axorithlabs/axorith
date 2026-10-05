using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Core.Models;
using Axorith.Core.Telemetry;
using Axorith.Telemetry;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using SessionSchedule = Axorith.Core.Models.SessionSchedule;
using ContractConfigurationLockStatus = Axorith.Contracts.ConfigurationLockStatus;

namespace Axorith.Host.Services;

public class SchedulerServiceImpl(IScheduleManager scheduleManager, ILogger<SchedulerServiceImpl> logger,
    ITelemetryService? telemetry = null, IPresetManager? presetManager = null)
    : SchedulerService.SchedulerServiceBase
{
    private readonly ITelemetryService _telemetry = telemetry ?? NoopTelemetryService.Instance;
    public override async Task<ListSchedulesResponse> ListSchedules(ListSchedulesRequest request,
        ServerCallContext context)
    {
        var schedules = await scheduleManager.ListSchedulesAsync(context.CancellationToken);
        var response = new ListSchedulesResponse();
        response.Schedules.AddRange(schedules.Select(schedule => ScheduleCodec.ToMessage(schedule)));
        return response;
    }

    public override async Task<Schedule> CreateSchedule(CreateScheduleRequest request, ServerCallContext context)
    {
        if (request.Schedule == null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Schedule is required"));
        }

        logger.LogInformation("Creating schedule '{Name}' for preset {PresetId}", request.Schedule.Name,
            request.Schedule.PresetId);

        var model = ScheduleCodec.ToModel(request.Schedule);
        await EnsurePresetMutableAsync(model.PresetId, context.CancellationToken);
        model.Id = Guid.NewGuid();

        var saved = await scheduleManager.SaveScheduleAsync(model, context.CancellationToken);
        await TrackScheduleChangedAsync(saved, "create", context.CancellationToken).ConfigureAwait(false);
        return ScheduleCodec.ToMessage(saved);
    }

    public override async Task<Schedule> UpdateSchedule(UpdateScheduleRequest request, ServerCallContext context)
    {
        if (request.Schedule == null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Schedule is required"));
        }

        logger.LogInformation("Updating schedule '{Name}' ({Id})", request.Schedule.Name, request.Schedule.Id);

        var model = ScheduleCodec.ToModel(request.Schedule);
        var existing = await GetMutableScheduleAsync(model.Id, context.CancellationToken);
        var configurationChanged = existing is null || !SchedulesEqual(existing, model);
        await EnsurePresetMutableAsync(model.PresetId, context.CancellationToken);
        var saved = await scheduleManager.SaveScheduleAsync(model, context.CancellationToken);
        if (configurationChanged)
            await TrackScheduleChangedAsync(saved, "update", context.CancellationToken).ConfigureAwait(false);
        return ScheduleCodec.ToMessage(saved);
    }

    public override async Task<Empty> DeleteSchedule(DeleteScheduleRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ScheduleId, out var id))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid Schedule ID"));
        }

        logger.LogInformation("Deleting schedule {Id}", id);

        var existing = await GetMutableScheduleAsync(id, context.CancellationToken);
        await scheduleManager.DeleteScheduleAsync(id, context.CancellationToken);
        if (existing is not null)
        {
            await TrackScheduleChangedAsync(existing, "delete", context.CancellationToken).ConfigureAwait(false);
        }
        return new Empty();
    }

    public override async Task<Schedule> SetEnabled(SetScheduleEnabledRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ScheduleId, out var id))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid Schedule ID"));
        }

        var existing = await GetMutableScheduleAsync(id, context.CancellationToken);
        logger.LogInformation("Setting schedule {Id} enabled: {Enabled}", id, request.Enabled);

        var updated = await scheduleManager.SetEnabledAsync(id, request.Enabled, context.CancellationToken);

        if (updated == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Schedule not found"));
        }

        if (existing?.IsEnabled != updated.IsEnabled)
        {
            await TrackScheduleChangedAsync(updated, "update", context.CancellationToken).ConfigureAwait(false);
        }

        return ScheduleCodec.ToMessage(updated);
    }

    public override async Task<ContractConfigurationLockStatus> GetConfigurationLockStatus(
        ConfigurationLockStatusRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.PresetId, out var presetId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid preset ID."));
        }

        var status = await scheduleManager.GetConfigurationLockStatusAsync(presetId, context.CancellationToken);
        return new ContractConfigurationLockStatus
        {
            IsLocked = status.IsLocked,
            SecondsUntilStart = (long)(status.StartsIn?.TotalSeconds ?? 0)
        };
    }

    private async Task<SessionSchedule?> GetMutableScheduleAsync(Guid id, CancellationToken cancellationToken)
    {
        var schedule = (await scheduleManager.ListSchedulesAsync(cancellationToken)).FirstOrDefault(s => s.Id == id);
        if (schedule != null)
            await EnsurePresetMutableAsync(schedule.PresetId, cancellationToken);
        return schedule;
    }

    private async Task EnsurePresetMutableAsync(Guid presetId, CancellationToken cancellationToken)
    {
        var status = await scheduleManager.GetConfigurationLockStatusAsync(presetId, cancellationToken);
        if (status.IsLocked)
        {
            var time = status.StartsIn is { } remaining && remaining > TimeSpan.Zero
                ? $"Session starts in {(int)Math.Ceiling(remaining.TotalMinutes)} min"
                : "Session starts now";
            throw new RpcException(new Status(StatusCode.FailedPrecondition, $"Configuration locked · {time}."));
        }
    }

    private async Task TrackScheduleChangedAsync(SessionSchedule schedule, string changeType,
        CancellationToken cancellationToken)
    {
        var properties = ProductAnalyticsProperties.Schedule(schedule, changeType);
        if (presetManager is not null)
        {
            try
            {
                var presets = await presetManager.LoadAllPresetsAsync(cancellationToken).ConfigureAwait(false);
                var schedules = await scheduleManager.ListSchedulesAsync(cancellationToken).ConfigureAwait(false);
                properties["$set"] = ProductAnalyticsProperties.ProductState(presets, schedules);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not refresh telemetry product-state properties after schedule change.");
            }
        }
        _telemetry.TrackEvent("ScheduleChanged", properties);
    }

    private static bool SchedulesEqual(SessionSchedule left, SessionSchedule right) =>
        left.PresetId == right.PresetId && string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        left.IsEnabled == right.IsEnabled && left.Type == right.Type && left.OneTimeDate == right.OneTimeDate &&
        left.RecurringTime == right.RecurringTime && left.DaysOfWeek.Distinct().Order().SequenceEqual(
            right.DaysOfWeek.Distinct().Order()) && left.AutoStopDuration == right.AutoStopDuration &&
        left.NextPresetId == right.NextPresetId && left.Use24HourFormat == right.Use24HourFormat;
}
