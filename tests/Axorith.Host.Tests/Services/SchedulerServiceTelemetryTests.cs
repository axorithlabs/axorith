using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Services;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Axorith.Host.Tests.Services;

public sealed class SchedulerServiceTelemetryTests
{
    [Fact]
    public async Task ScheduleUpdate_EmitsOnlyWhenConfigurationChanges()
    {
        var handler = new CapturingHttpHandler();
        await using var telemetry = new RecordingTelemetryService(new TelemetryService(new TelemetrySettings
        {
            Enabled = true,
            PostHogApiKey = "test-project-key",
            PostHogHost = "http://localhost/",
            DistinctId = Guid.NewGuid().ToString("D"),
            ApplicationName = "Axorith.Host",
            BatchSize = 100,
            FlushInterval = TimeSpan.FromHours(1)
        }, new CapturingHttpClientFactory(handler)));

        var schedule = new SessionSchedule
        {
            Id = Guid.NewGuid(),
            PresetId = Guid.NewGuid(),
            Name = "Daily start",
            Type = ScheduleType.Recurring,
            RecurringTime = TimeSpan.FromHours(9),
            DaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Wednesday],
            LastRun = DateTimeOffset.UtcNow.AddDays(-1)
        };
        var manager = new InMemoryScheduleManager(schedule);
        var preset = new SessionPreset(schedule.PresetId);
        var presetManager = new InMemoryPresetManager(preset);
        var service = new SchedulerServiceImpl(manager, NullLogger<SchedulerServiceImpl>.Instance, telemetry,
            presetManager);

        await service.UpdateSchedule(new UpdateScheduleRequest { Schedule = ScheduleCodec.ToMessage(schedule) },
            GrpcTestContext.Create());
        Assert.DoesNotContain("ScheduleChanged", telemetry.EventNames);

        var changed = ScheduleCodec.ToMessage(schedule);
        changed.RecurringTime = "09:30";
        changed.IsEnabled = false;
        await service.UpdateSchedule(new UpdateScheduleRequest { Schedule = changed }, GrpcTestContext.Create());
        await telemetry.FlushAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        Assert.Contains("ScheduleChanged", telemetry.EventNames);

        var scheduleChanged = Assert.Single(ReadEvents(handler),
            item => item.GetProperty("event").GetString() == "ScheduleChanged");
        Assert.Equal("update", scheduleChanged.GetProperty("properties").GetProperty("changeType").GetString());
        Assert.Equal(570, scheduleChanged.GetProperty("properties").GetProperty("scheduledMinuteOfDay").GetInt32());
        var state = scheduleChanged.GetProperty("properties").GetProperty("$set");
        Assert.Equal(1, state.GetProperty("presetCount").GetInt32());
        Assert.Equal(1, state.GetProperty("scheduleCount").GetInt32());
        Assert.Equal(0, state.GetProperty("enabledScheduleCount").GetInt32());
    }

    private static JsonElement[] ReadEvents(CapturingHttpHandler handler) => handler.Payloads
        .SelectMany(body =>
        {
            using var payload = JsonDocument.Parse(body);
            return payload.RootElement.GetProperty("batch").EnumerateArray().Select(item => item.Clone()).ToArray();
        })
        .ToArray();

    private sealed class InMemoryScheduleManager(SessionSchedule initial) : IScheduleManager
    {
        private readonly List<SessionSchedule> _schedules = [initial];

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartProcessingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<SessionSchedule>> ListSchedulesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SessionSchedule>>(_schedules.ToArray());
        public Task<IReadOnlyList<SessionSchedule>> GetSchedulesForPresetAsync(Guid presetId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SessionSchedule>>(
            _schedules.Where(schedule => schedule.PresetId == presetId).ToArray());

        public Task<SessionSchedule> SaveScheduleAsync(SessionSchedule schedule, CancellationToken cancellationToken)
        {
            _schedules.RemoveAll(current => current.Id == schedule.Id);
            _schedules.Add(schedule);
            return Task.FromResult(schedule);
        }

        public Task DeleteScheduleAsync(Guid scheduleId, CancellationToken cancellationToken)
        {
            _schedules.RemoveAll(schedule => schedule.Id == scheduleId);
            return Task.CompletedTask;
        }

        public Task<SessionSchedule?> SetEnabledAsync(Guid scheduleId, bool enabled,
            CancellationToken cancellationToken)
        {
            var schedule = _schedules.FirstOrDefault(current => current.Id == scheduleId);
            if (schedule is not null) schedule.IsEnabled = enabled;
            return Task.FromResult(schedule);
        }

        public Task<Axorith.Core.Models.ConfigurationLockStatus> GetConfigurationLockStatusAsync(Guid presetId,
            CancellationToken cancellationToken) => Task.FromResult(
            new Axorith.Core.Models.ConfigurationLockStatus(false, null));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InMemoryPresetManager(params SessionPreset[] presets) : IPresetManager
    {
        private readonly Dictionary<Guid, SessionPreset> _presets = presets.ToDictionary(preset => preset.Id);
        public Task<IReadOnlyList<SessionPreset>> LoadAllPresetsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SessionPreset>>(_presets.Values.ToArray());
        public Task<SessionPreset?> GetPresetByIdAsync(Guid presetId, CancellationToken cancellationToken) =>
            Task.FromResult(_presets.GetValueOrDefault(presetId));
        public Task SavePresetAsync(SessionPreset preset, CancellationToken cancellationToken)
        {
            _presets[preset.Id] = preset;
            return Task.CompletedTask;
        }
        public Task DeletePresetAsync(Guid presetId, CancellationToken cancellationToken)
        {
            _presets.Remove(presetId);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingHttpClientFactory(CapturingHttpHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingTelemetryService(ITelemetryService inner) : ITelemetryService
    {
        public List<string> EventNames { get; } = [];
        public bool IsEnabled => inner.IsEnabled;
        public void SetEnabled(bool enabled) => inner.SetEnabled(enabled);
        public void TrackEvent(string eventName, IReadOnlyDictionary<string, object?>? properties = null)
        {
            EventNames.Add(eventName);
            inner.TrackEvent(eventName, properties);
        }

        public void TrackError(Exception exception, string subsystem, string operation, string severity,
            bool handled, bool fatal, IReadOnlyDictionary<string, object?>? properties = null) =>
            inner.TrackError(exception, subsystem, operation, severity, handled, fatal, properties);

        public Task FlushAsync(CancellationToken cancellationToken = default) => inner.FlushAsync(cancellationToken);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class CapturingHttpHandler : HttpMessageHandler
    {
        public ConcurrentQueue<string> Payloads { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Payloads.Enqueue(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
        }
    }
}
