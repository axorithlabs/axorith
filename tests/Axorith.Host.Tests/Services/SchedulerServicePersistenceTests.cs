using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Services;
using Axorith.Sdk.Services;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Axorith.Host.Tests.Services;

public sealed class SchedulerServicePersistenceTests
{
    [Fact]
    public async Task EnabledAndConfigurationChangesSurviveRestart()
    {
        var directory = Directory.CreateTempSubdirectory("axorith-toggle-schedule-");
        try
        {
            var presets = new PresetManager(Path.Combine(directory.FullName, "presets"), NullLogger<PresetManager>.Instance);
            var preset = new SessionPreset(Guid.NewGuid()) { Name = "Scheduled workspace" };
            await presets.SavePresetAsync(preset, CancellationToken.None);
            var session = Mock.Of<ISessionManager>();
            var autoStop = Mock.Of<ISessionAutoStopService>();
            var notifier = Mock.Of<INotifier>();
            await using var manager = new ScheduleManager(directory.FullName, session, presets, autoStop, notifier,
                NullLogger<ScheduleManager>.Instance);
            var schedule = new SessionSchedule
            {
                Id = Guid.NewGuid(), PresetId = preset.Id, Name = "Tomorrow", IsEnabled = true,
                Type = ScheduleType.OneTime, OneTimeDate = DateTimeOffset.UtcNow.AddDays(1)
            };
            await manager.SaveScheduleAsync(schedule, CancellationToken.None);
            var service = new SchedulerServiceImpl(manager, NullLogger<SchedulerServiceImpl>.Instance, NoopTelemetryService.Instance, presets);
            var request = new SetScheduleEnabledRequest { ScheduleId = schedule.Id.ToString(), Enabled = false };

            Assert.False((await service.SetEnabled(request, GrpcTestContext.Create())).IsEnabled);
            Assert.False((await service.SetEnabled(request, GrpcTestContext.Create())).IsEnabled);
            await using (var restored = new ScheduleManager(directory.FullName, session, presets, autoStop, notifier,
                             NullLogger<ScheduleManager>.Instance))
            {
                await restored.StartAsync(CancellationToken.None);
                Assert.False(Assert.Single(await restored.ListSchedulesAsync(CancellationToken.None)).IsEnabled);
            }
            request.Enabled = true;
            Assert.True((await service.SetEnabled(request, GrpcTestContext.Create())).IsEnabled);
            var changed = Axorith.Contracts.ScheduleCodec.ToMessage(schedule);
            changed.Name = "Changed workspace schedule";
            changed.OneTimeDate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(2));
            await service.UpdateSchedule(new UpdateScheduleRequest { Schedule = changed }, GrpcTestContext.Create());
            await using var fresh = new ScheduleManager(directory.FullName, session, presets, autoStop, notifier,
                NullLogger<ScheduleManager>.Instance);
            await fresh.StartAsync(CancellationToken.None);
            var persisted = Assert.Single(await fresh.ListSchedulesAsync(CancellationToken.None));
            Assert.Equal(schedule.Id, persisted.Id);
            Assert.True(persisted.IsEnabled);
            Assert.Equal(changed.Name, persisted.Name);
            Assert.Equal(changed.OneTimeDate.ToDateTimeOffset(), persisted.OneTimeDate);
        }
        finally { directory.Delete(recursive: true); }
    }

}
