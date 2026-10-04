using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Contracts;
using Axorith.Core.Models;
using Polly.Retry;
using ConfigurationLockStatusModel = Axorith.Core.Models.ConfigurationLockStatus;

namespace Axorith.Client.CoreSdk;

internal class GrpcSchedulerApi(SchedulerService.SchedulerServiceClient client, AsyncRetryPolicy retryPolicy)
    : ISchedulerApi
{
    public async Task<IReadOnlyList<SessionSchedule>> ListSchedulesAsync(CancellationToken ct = default) =>
        await retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.ListSchedulesAsync(new ListSchedulesRequest(), cancellationToken: ct)
                .ConfigureAwait(false);
            return response.Schedules.Select(message => ScheduleCodec.ToModel(message, includeLastRun: true)).ToList();
        }).ConfigureAwait(false);

    public Task<ConfigurationLockStatusModel> GetConfigurationLockStatusAsync(Guid presetId,
        CancellationToken ct = default) => retryPolicy.ExecuteAsync(async () =>
    {
        var response = await client.GetConfigurationLockStatusAsync(
                new ConfigurationLockStatusRequest { PresetId = presetId.ToString() }, cancellationToken: ct)
            .ConfigureAwait(false);
        return new ConfigurationLockStatusModel(response.IsLocked,
            response.IsLocked ? TimeSpan.FromSeconds(response.SecondsUntilStart) : null);
    });

    public Task<SessionSchedule> CreateScheduleAsync(SessionSchedule schedule, CancellationToken ct = default) =>
        retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.CreateScheduleAsync(
                    new CreateScheduleRequest { Schedule = ScheduleCodec.ToMessage(schedule) }, cancellationToken: ct)
                .ConfigureAwait(false);
            return ScheduleCodec.ToModel(response, includeLastRun: true);
        });

    public Task<SessionSchedule> UpdateScheduleAsync(SessionSchedule schedule, CancellationToken ct = default) =>
        retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.UpdateScheduleAsync(
                    new UpdateScheduleRequest { Schedule = ScheduleCodec.ToMessage(schedule) }, cancellationToken: ct)
                .ConfigureAwait(false);
            return ScheduleCodec.ToModel(response, includeLastRun: true);
        });

    public Task DeleteScheduleAsync(Guid scheduleId, CancellationToken ct = default) =>
        retryPolicy.ExecuteAsync(async () =>
        {
            await client.DeleteScheduleAsync(new DeleteScheduleRequest { ScheduleId = scheduleId.ToString() },
                    cancellationToken: ct)
                .ConfigureAwait(false);
        });

    public Task<SessionSchedule> SetEnabledAsync(Guid scheduleId, bool enabled, CancellationToken ct = default) =>
        retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.SetEnabledAsync(new SetScheduleEnabledRequest
            {
                ScheduleId = scheduleId.ToString(),
                Enabled = enabled
            }, cancellationToken: ct).ConfigureAwait(false);
            return ScheduleCodec.ToModel(response, includeLastRun: true);
        });
}
