using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Mappers;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Axorith.Host.Services;

public class SchedulerServiceImpl(IScheduleManager scheduleManager, ILogger<SchedulerServiceImpl> logger)
    : SchedulerService.SchedulerServiceBase
{
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
            model.Id = Guid.NewGuid();

            var saved = await scheduleManager.SaveScheduleAsync(model, context.CancellationToken);
            return ScheduleMapper.ToMessage(saved);
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
            var saved = await scheduleManager.SaveScheduleAsync(model, context.CancellationToken);
            return ScheduleMapper.ToMessage(saved);
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

            await scheduleManager.DeleteScheduleAsync(id, context.CancellationToken);
            return new Empty();
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

            logger.LogInformation("Setting schedule {Id} enabled: {Enabled}", id, request.Enabled);

            var updated = await scheduleManager.SetEnabledAsync(id, request.Enabled, context.CancellationToken);

            if (updated == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "Schedule not found"));
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
}