using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Mappers;
using Axorith.Telemetry;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Axorith.Host.Services;

public class SchedulerServiceImpl(IScheduleManager scheduleManager, ILogger<SchedulerServiceImpl> logger,
    ITelemetryService? telemetry = null)
    : SchedulerService.SchedulerServiceBase
{
    private readonly ITelemetryService _telemetry = telemetry ?? new NoopTelemetryService();
    public override async Task<ListSchedulesResponse> ListSchedules(ListSchedulesRequest request,
        ServerCallContext context)
    {
        try
        {
            var schedules = await scheduleManager.ListSchedulesAsync(context.CancellationToken);
            var response = new ListSchedulesResponse();
            response.Schedules.AddRange(schedules.Select(ScheduleMapper.ToMessage));
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing schedules");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to list schedules"));
        }
    }

    public override async Task<Schedule> CreateSchedule(CreateScheduleRequest request, ServerCallContext context)
    {
        try
        {
            if (request.Schedule == null)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Schedule is required"));
            }

            logger.LogInformation("Creating schedule '{Name}' for preset {PresetId}", request.Schedule.Name,
                request.Schedule.PresetId);

            var model = ScheduleMapper.ToModel(request.Schedule);
            await EnsurePresetMutableAsync(model.PresetId, context.CancellationToken);
            model.Id = Guid.NewGuid();

            var saved = await scheduleManager.SaveScheduleAsync(model, context.CancellationToken);
            TrackScheduleChanged(saved.Id, "create", saved.IsEnabled);
            return ScheduleMapper.ToMessage(saved);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating schedule");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to create schedule"));
        }
    }

    public override async Task<Schedule> UpdateSchedule(UpdateScheduleRequest request, ServerCallContext context)
    {
        try
        {
            if (request.Schedule == null)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Schedule is required"));
            }

            logger.LogInformation("Updating schedule '{Name}' ({Id})", request.Schedule.Name, request.Schedule.Id);

            var model = ScheduleMapper.ToModel(request.Schedule);
            var existing = (await scheduleManager.ListSchedulesAsync(context.CancellationToken))
                .FirstOrDefault(s => s.Id == model.Id);
            if (existing != null)
            {
                await EnsurePresetMutableAsync(existing.PresetId, context.CancellationToken);
            }

            await EnsurePresetMutableAsync(model.PresetId, context.CancellationToken);
            var saved = await scheduleManager.SaveScheduleAsync(model, context.CancellationToken);
            TrackScheduleChanged(saved.Id, "update", saved.IsEnabled);
            return ScheduleMapper.ToMessage(saved);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating schedule");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to update schedule"));
        }
    }

    public override async Task<Empty> DeleteSchedule(DeleteScheduleRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.ScheduleId, out var id))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid Schedule ID"));
            }

            logger.LogInformation("Deleting schedule {Id}", id);

            var existing = (await scheduleManager.ListSchedulesAsync(context.CancellationToken))
                .FirstOrDefault(s => s.Id == id);
            if (existing != null)
            {
                await EnsurePresetMutableAsync(existing.PresetId, context.CancellationToken);
            }

            await scheduleManager.DeleteScheduleAsync(id, context.CancellationToken);
            if (existing is not null)
            {
                TrackScheduleChanged(id, "delete");
            }
            return new Empty();
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting schedule");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to delete schedule"));
        }
    }

    public override async Task<Schedule> SetEnabled(SetScheduleEnabledRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.ScheduleId, out var id))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid Schedule ID"));
            }

            var existing = (await scheduleManager.ListSchedulesAsync(context.CancellationToken))
                .FirstOrDefault(s => s.Id == id);
            if (existing != null)
            {
                await EnsurePresetMutableAsync(existing.PresetId, context.CancellationToken);
            }

            logger.LogInformation("Setting schedule {Id} enabled: {Enabled}", id, request.Enabled);

            var updated = await scheduleManager.SetEnabledAsync(id, request.Enabled, context.CancellationToken);

            if (updated == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "Schedule not found"));
            }

            if (existing?.IsEnabled != updated.IsEnabled)
            {
                TrackScheduleChanged(id, "update", updated.IsEnabled);
            }

            return ScheduleMapper.ToMessage(updated);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error toggling schedule");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to toggle schedule"));
        }
    }

    public override async Task<ConfigurationLockStatus> GetConfigurationLockStatus(
        ConfigurationLockStatusRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.PresetId, out var presetId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid preset ID."));
        }

        var status = await scheduleManager.GetConfigurationLockStatusAsync(presetId, context.CancellationToken);
        return new ConfigurationLockStatus
        {
            IsLocked = status.IsLocked,
            SecondsUntilStart = (long)(status.StartsIn?.TotalSeconds ?? 0)
        };
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

    private void TrackScheduleChanged(Guid scheduleId, string changeType, bool? enabled = null)
    {
        var properties = new Dictionary<string, object?>
        {
            ["scheduleId"] = scheduleId,
            ["changeType"] = changeType
        };
        if (enabled.HasValue) properties["enabled"] = enabled.Value;
        _telemetry.TrackEvent("ScheduleChanged", properties);
    }
}
