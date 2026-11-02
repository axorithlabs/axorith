using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Contracts;
using Axorith.Core.Models;
using Polly.Retry;
using ConfigurationLockStatusModel = Axorith.Core.Models.ConfigurationLockStatus;

namespace Axorith.Client.CoreSdk;

internal class GrpcSchedulerApi(SchedulerService.SchedulerServiceClient client, AsyncRetryPolicy retryPolicy)
    : ISchedulerApi
{
    public Task<IReadOnlyList<SessionSchedule>> ListSchedulesAsync(CancellationToken ct = default) =>
        GrpcResultMapper.ExecuteAsync<ListSchedulesResponse, IReadOnlyList<SessionSchedule>>(retryPolicy,
            () => client.ListSchedulesAsync(new ListSchedulesRequest(), cancellationToken: ct),
            response => [.. response.Schedules.Select(message => ScheduleCodec.ToModel(message, includeLastRun: true))]);

    public Task<ConfigurationLockStatusModel> GetConfigurationLockStatusAsync(Guid presetId,
        CancellationToken ct = default) => GrpcResultMapper.ExecuteAsync(retryPolicy,
        () => client.GetConfigurationLockStatusAsync(
            new ConfigurationLockStatusRequest { PresetId = presetId.ToString() }, cancellationToken: ct),
        response => new ConfigurationLockStatusModel(response.IsLocked,
            response.IsLocked ? TimeSpan.FromSeconds(response.SecondsUntilStart) : null));

    public Task<SessionSchedule> CreateScheduleAsync(SessionSchedule schedule, CancellationToken ct = default) =>
        GrpcResultMapper.ExecuteAsync(retryPolicy, () => client.CreateScheduleAsync(
            new CreateScheduleRequest { Schedule = ScheduleCodec.ToMessage(schedule) }, cancellationToken: ct),
            response => ScheduleCodec.ToModel(response, includeLastRun: true));

    public Task<SessionSchedule> UpdateScheduleAsync(SessionSchedule schedule, CancellationToken ct = default) =>
        GrpcResultMapper.ExecuteAsync(retryPolicy, () => client.UpdateScheduleAsync(
            new UpdateScheduleRequest { Schedule = ScheduleCodec.ToMessage(schedule) }, cancellationToken: ct),
            response => ScheduleCodec.ToModel(response, includeLastRun: true));

    public Task DeleteScheduleAsync(Guid scheduleId, CancellationToken ct = default) =>
        GrpcResultMapper.ExecuteAsync(retryPolicy, () => client.DeleteScheduleAsync(
            new DeleteScheduleRequest { ScheduleId = scheduleId.ToString() }, cancellationToken: ct), response => response);

    public Task<SessionSchedule> SetEnabledAsync(Guid scheduleId, bool enabled, CancellationToken ct = default) =>
        GrpcResultMapper.ExecuteAsync(retryPolicy, () => client.SetEnabledAsync(new SetScheduleEnabledRequest
            {
                ScheduleId = scheduleId.ToString(),
                Enabled = enabled
            }, cancellationToken: ct), response => ScheduleCodec.ToModel(response, includeLastRun: true));
}
