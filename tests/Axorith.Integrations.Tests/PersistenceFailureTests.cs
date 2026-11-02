using Axorith.Contracts;
using Axorith.Core.Models;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Xunit;

namespace Axorith.Integrations.Tests;

public partial class HostGrpcEndToEndTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockedPresetFileReturnsAnErrorAndPreservesThePreviousPreset(bool delete)
    {
        var clients = await CreateAuthenticatedClientsAsync();
        using var channel = clients.channel;
        var preset = await clients.presets.CreatePresetAsync(new CreatePresetRequest
        { Preset = new Preset { Name = "Durable preset " + Guid.NewGuid() } });
        var path = Path.Combine(factory.TestDataPath, "presets", preset.Id + ".json");
        var original = await File.ReadAllTextAsync(path);
        try
        {
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (delete)
                    await Assert.ThrowsAsync<RpcException>(() => clients.presets.DeletePresetAsync(new DeletePresetRequest { PresetId = preset.Id }).ResponseAsync);
                else
                {
                    preset.Name = "Unsaved update " + Guid.NewGuid();
                    await Assert.ThrowsAsync<RpcException>(() => clients.presets.UpdatePresetAsync(new UpdatePresetRequest { Preset = preset }).ResponseAsync);
                }
                Assert.Equal(original, await File.ReadAllTextAsync(path));
            }
            Assert.Equal(original, await File.ReadAllTextAsync(path));
        }
        finally { await clients.presets.DeletePresetAsync(new DeletePresetRequest { PresetId = preset.Id }); }
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("toggle")]
    public async Task FailedScheduleWriteCannotReportSuccessOrChangeLiveSchedules(string operation)
    {
        var clients = await CreateAuthenticatedClientsAsync();
        using var channel = clients.channel;
        var api = new SchedulerService.SchedulerServiceClient(channel);
        var preset = await clients.presets.CreatePresetAsync(new CreatePresetRequest
        { Preset = new Preset { Name = "Durable schedule " + Guid.NewGuid() } });
        var schedule = await CreateScheduleAsync(api, preset.Id, "Original schedule", ScheduleType.OneTime,
            value => value.OneTimeDate = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(1)));
        var path = Path.Combine(factory.TestDataPath, "config", "schedules.json");
        var originalFile = await File.ReadAllTextAsync(path);
        var originalLive = (await api.ListSchedulesAsync(new ListSchedulesRequest())).Schedules.Select(value => value.ToString()).ToArray();
        try
        {
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                switch (operation)
                {
                    case "create":
                        var added = schedule.Clone();
                        added.Id = Guid.NewGuid().ToString();
                        added.Name = "Unsaved new schedule";
                        await Assert.ThrowsAsync<RpcException>(() => api.CreateScheduleAsync(new CreateScheduleRequest { Schedule = added }).ResponseAsync);
                        break;
                    case "update":
                        var changed = schedule.Clone();
                        changed.Name = "Unsaved change";
                        await Assert.ThrowsAsync<RpcException>(() => api.UpdateScheduleAsync(new UpdateScheduleRequest { Schedule = changed }).ResponseAsync);
                        break;
                    case "delete":
                        await Assert.ThrowsAsync<RpcException>(() => api.DeleteScheduleAsync(new DeleteScheduleRequest { ScheduleId = schedule.Id }).ResponseAsync);
                        break;
                    case "toggle":
                        await Assert.ThrowsAsync<RpcException>(() => api.SetEnabledAsync(new SetScheduleEnabledRequest { ScheduleId = schedule.Id, Enabled = false }).ResponseAsync);
                        break;
                }
                Assert.Equal(originalFile, await File.ReadAllTextAsync(path));
                Assert.Equal(originalLive, (await api.ListSchedulesAsync(new ListSchedulesRequest())).Schedules.Select(value => value.ToString()).ToArray());
            }
        }
        finally
        {
            foreach (var item in (await api.ListSchedulesAsync(new ListSchedulesRequest())).Schedules.Where(value => value.PresetId == preset.Id))
                await api.DeleteScheduleAsync(new DeleteScheduleRequest { ScheduleId = item.Id });
            await clients.presets.DeletePresetAsync(new DeletePresetRequest { PresetId = preset.Id });
        }
    }
}
