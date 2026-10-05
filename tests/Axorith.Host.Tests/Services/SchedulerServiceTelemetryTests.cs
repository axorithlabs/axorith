using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Services;
using Axorith.Telemetry;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Axorith.Host.Tests.Services;

public sealed class SchedulerServiceTelemetryTests
{
    [Fact]
    public async Task ScheduleUpdate_EmitsOnlyWhenConfigurationChanges()
    {
        await using var server = new PostHogCaptureServer();
        await using var telemetry = new RecordingTelemetryService(new TelemetryService(new TelemetrySettings
        {
            Enabled = true,
            PostHogApiKey = "test-project-key",
            PostHogHost = server.Host,
            DistinctId = Guid.NewGuid().ToString("D"),
            ApplicationName = "Axorith.Host",
            BatchSize = 100,
            FlushInterval = TimeSpan.FromHours(1)
        }));

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

        var scheduleChanged = Assert.Single(ReadEvents(server),
            item => item.GetProperty("event").GetString() == "ScheduleChanged");
        Assert.Equal("update", scheduleChanged.GetProperty("properties").GetProperty("changeType").GetString());
        Assert.Equal(570, scheduleChanged.GetProperty("properties").GetProperty("scheduledMinuteOfDay").GetInt32());
        var state = scheduleChanged.GetProperty("properties").GetProperty("$set");
        Assert.Equal(1, state.GetProperty("presetCount").GetInt32());
        Assert.Equal(1, state.GetProperty("scheduleCount").GetInt32());
        Assert.Equal(0, state.GetProperty("enabledScheduleCount").GetInt32());
    }

    private static JsonElement[] ReadEvents(PostHogCaptureServer server) => server.Payloads
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

        public void TrackError(Exception exception, string subsystem, string operation, string severity, bool handled,
            bool fatal, IReadOnlyDictionary<string, object?>? properties = null) =>
            inner.TrackError(exception, subsystem, operation, severity, handled, fatal, properties);

        public Task FlushAsync(CancellationToken cancellationToken = default) => inner.FlushAsync(cancellationToken);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class PostHogCaptureServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentQueue<string> _payloads = new();
        private readonly Task _listenTask;

        public string Host { get; }
        public IReadOnlyCollection<string> Payloads => _payloads.ToArray();

        public PostHogCaptureServer()
        {
            _listener.Start();
            Host = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _listenTask = ListenAsync(_stop.Token);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try
            {
                await _listenTask.ConfigureAwait(false);
            }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            _stop.Dispose();
        }

        private async Task ListenAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
                    var contentLength = 0;
                    var isChunked = false;
                    while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { Length: > 0 } line)
                    {
                        var separator = line.IndexOf(':');
                        if (separator < 0) continue;
                        var headerName = line[..separator];
                        var headerValue = line[(separator + 1)..].Trim();
                        if (headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                            contentLength = int.Parse(headerValue, CultureInfo.InvariantCulture);
                        else if (headerName.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) &&
                                 headerValue.Contains("chunked", StringComparison.OrdinalIgnoreCase))
                            isChunked = true;
                    }

                    _payloads.Enqueue(isChunked
                        ? await ReadChunkedBodyAsync(reader, cancellationToken).ConfigureAwait(false)
                        : await ReadFixedBodyAsync(reader, contentLength, cancellationToken).ConfigureAwait(false));
                    await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (SocketException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            }
        }

        private static async Task<string> ReadFixedBodyAsync(StreamReader reader, int contentLength,
            CancellationToken cancellationToken)
        {
            var body = new char[contentLength];
            var read = 0;
            while (read < body.Length)
                read += await reader.ReadAsync(body.AsMemory(read), cancellationToken).ConfigureAwait(false);
            return new string(body);
        }

        private static async Task<string> ReadChunkedBodyAsync(StreamReader reader,
            CancellationToken cancellationToken)
        {
            var body = new StringBuilder();
            while (true)
            {
                var sizeLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                var size = int.Parse(sizeLine!.Split(';', 2)[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (size == 0)
                {
                    while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { Length: > 0 }) { }
                    return body.ToString();
                }

                var chunk = new char[size];
                var read = 0;
                while (read < chunk.Length)
                    read += await reader.ReadAsync(chunk.AsMemory(read), cancellationToken).ConfigureAwait(false);
                body.Append(chunk);
                await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
