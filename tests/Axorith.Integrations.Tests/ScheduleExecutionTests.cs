using System.Diagnostics;
using System.Globalization;
using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Axorith.Integrations.Tests;

public partial class HostGrpcEndToEndTests
{
    [Theory]
    [InlineData(Axorith.Core.Models.AfterEndBehavior.LockPc)]
    [InlineData(Axorith.Core.Models.AfterEndBehavior.Sleep)]
    [InlineData(Axorith.Core.Models.AfterEndBehavior.SignOut)]
    [InlineData(Axorith.Core.Models.AfterEndBehavior.ShutDownPc)]
    public async Task TestHostRejectsSystemActionsBeforeReachingWindows(Axorith.Core.Models.AfterEndBehavior behavior)
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable("AXORITH_TEST_MODE"));
        var service = factory.Services.GetRequiredService<ISessionAutoStopService>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAfterEndActionAsync(behavior));
        Assert.Equal("System power and sign-in actions are disabled in test mode.", error.Message);
    }

    [WindowsFact]
    public async Task ForeignStopScheduleCannotEndOrChainTheActiveWorkspace()
    {
        var clients = await CreateAuthenticatedClientsAsync();
        using var channel = clients.channel;
        var scheduler = new SchedulerService.SchedulerServiceClient(channel);
        var a = await CreateAppBlockerPresetAsync(clients.presets, "Workspace A");
        var b = await CreateAppBlockerPresetAsync(clients.presets, "Workspace B");
        var scheduleIds = new List<string>();
        try
        {
            var started = await clients.sessions.StartSessionAsync(new StartSessionRequest { PresetId = a.Id });
            Assert.True(started.Success, started.Message);
            var manager = factory.Services.GetRequiredService<ISessionManager>();
            var instanceId = manager.CurrentSessionInstanceId;
            var foreign = await CreateScheduleAsync(scheduler, b.Id, "Foreign stop", ScheduleType.StopRecurring,
                value =>
                {
                    value.RecurringTime = DateTimeOffset.Now.AddSeconds(2).TimeOfDay.ToString("c", CultureInfo.InvariantCulture);
                    value.NextPresetId = b.Id;
                });
            scheduleIds.Add(foreign.Id);

            var autoStop = factory.Services.GetRequiredService<ISessionAutoStopService>();
            Assert.False(await autoStop.CompleteNaturallyAsync(manager.ActiveSession!, Guid.Parse(b.Id),
                schedule: ScheduleCodec.ToModel(foreign)));

            // Stay active beyond B's due time, then prove the same scheduler can execute A's own stop.
            var observe = Stopwatch.StartNew();
            while (observe.Elapsed < TimeSpan.FromSeconds(4))
            {
                var state = await clients.sessions.GetSessionStateAsync(new GetSessionStateRequest());
                Assert.True(state.IsActive);
                Assert.Equal(a.Id, state.PresetId);
                Assert.Equal(instanceId, manager.CurrentSessionInstanceId);
                await Task.Delay(100);
            }

            var own = await CreateScheduleAsync(scheduler, a.Id, "Own stop", ScheduleType.StopRecurring,
                value => value.RecurringTime = DateTimeOffset.Now.AddSeconds(2).TimeOfDay.ToString("c", CultureInfo.InvariantCulture));
            scheduleIds.Add(own.Id);
            var ended = await WaitForSessionStateAsync(clients.sessions, state => !state.IsActive, TimeSpan.FromSeconds(8));
            Assert.False(ended.IsActive);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while ((await scheduler.ListSchedulesAsync(new ListSchedulesRequest(), cancellationToken: deadline.Token))
                   .Schedules.Single(value => value.Id == own.Id).LastRun is null)
                await Task.Delay(25, deadline.Token);
        }
        finally
        {
            await clients.sessions.StopSessionAsync(new StopSessionRequest());
            foreach (var id in scheduleIds)
                await scheduler.DeleteScheduleAsync(new DeleteScheduleRequest { ScheduleId = id });
            await clients.presets.DeletePresetAsync(new DeletePresetRequest { PresetId = a.Id });
            await clients.presets.DeletePresetAsync(new DeletePresetRequest { PresetId = b.Id });
        }
    }
}
