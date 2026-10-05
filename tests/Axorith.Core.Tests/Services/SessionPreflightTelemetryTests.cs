using System.Text.Json;
using Autofac;
using Axorith.Core.Models;
using Axorith.Core.Tests.Helpers;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Core.Telemetry;
using Axorith.Sdk;
using Axorith.Sdk.Services;
using Axorith.Shared.Exceptions;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Core.Tests.Services;

public sealed class SessionPreflightTelemetryTests
{
    [Fact]
    public async Task ManualStart_EmitsOneCanonicalPreflightEventFromHostForTheAttempt()
    {
        await using var server = new PostHogTestServer();

        var directory = Directory.CreateTempSubdirectory("axorith-session-telemetry-");
        try
        {
            var distinctId = Guid.NewGuid().ToString("D");
            await using var telemetry = CreateTelemetry(server, distinctId);

            var presetId = Guid.NewGuid();
            var moduleId = Guid.NewGuid();
            var sessionInstanceId = Guid.NewGuid();
            var preset = new SessionPreset(presetId)
            {
                Name = "Telemetry test",
                Modules = [new ConfiguredModule { ModuleId = moduleId }],
                FocusCommitment = new FocusCommitmentOptions
                {
                    Mode = FocusCommitmentMode.Locked,
                    EndCondition = FocusEndCondition.Duration,
                    Duration = TimeSpan.FromMinutes(1),
                    BreakCount = 2,
                    BreakDuration = TimeSpan.FromMinutes(6),
                    AfterEnd = AfterEndBehavior.StartNextWorkspace,
                    NextWorkspaceId = Guid.NewGuid(),
                    ScheduleLockMinutes = 15
                }
            };

            await using var sessionManager = CreateSessionManager(moduleId, telemetry, directory.FullName);

            await sessionManager.StartSessionAsync(preset, startSource: "manual", sessionInstanceId: sessionInstanceId);
            await Assert.ThrowsAsync<SessionException>(() =>
                sessionManager.StartSessionAsync(preset, startSource: "manual", sessionInstanceId: sessionInstanceId));
            Assert.True(await sessionManager.EndCommittedSessionAsync(SessionEndReason.NaturalCompletion));

            using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await telemetry.FlushAsync(flushCts.Token);

            var events = ReadEvents(server)
                .ToArray();
            var names = events.Select(item => item.GetProperty("event").GetString()).ToArray();
            var preflightIndex = Array.IndexOf(names, "SessionPreflightCompleted");
            var startedIndex = Array.IndexOf(names, "SessionStarted");

            Assert.DoesNotContain("SessionStartRequested", names);
            Assert.DoesNotContain("ModuleStarted", names);
            Assert.DoesNotContain("ModuleStopped", names);
            Assert.Equal(1, names.Count(name => name == "SessionPreflightCompleted"));
            Assert.Equal(1, names.Count(name => name == "SessionStarted"));
            Assert.True(preflightIndex < startedIndex);
            var attemptEvents = events.Where(item => item.GetProperty("event").GetString() is
                "SessionPreflightCompleted" or "SessionStarted");
            Assert.All(attemptEvents, item =>
            {
                Assert.Equal(distinctId, item.GetProperty("distinct_id").GetString());
                Assert.Equal(sessionInstanceId.ToString("D"),
                    item.GetProperty("properties").GetProperty("sessionInstanceId").GetString());
            });
            var started = events.Single(item => item.GetProperty("event").GetString() == "SessionStarted")
                .GetProperty("properties");
            var stopped = events.Single(item => item.GetProperty("event").GetString() == "SessionStopped")
                .GetProperty("properties");
            Assert.Equal(60_000, started.GetProperty("plannedDurationMs").GetInt64());
            Assert.Equal(15, started.GetProperty("scheduleLockMinutes").GetInt32());
            Assert.Equal(360_000, started.GetProperty("breakDurationMs").GetInt64());
            Assert.True(started.GetProperty("hasNextWorkspace").GetBoolean());
            Assert.Equal("start_next_workspace", stopped.GetProperty("afterEndAction").GetString());
            Assert.Equal(15, stopped.GetProperty("scheduleLockMinutes").GetInt32());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScheduledStart_CarriesScheduleIdAcrossTheSessionLifecycle()
    {
        await using var server = new PostHogTestServer();
        var directory = Directory.CreateTempSubdirectory("axorith-scheduled-session-telemetry-");
        try
        {
            await using var telemetry = CreateTelemetry(server, Guid.NewGuid().ToString("D"));

            var moduleId = Guid.NewGuid();
            var scheduleId = Guid.NewGuid();
            var preset = new SessionPreset(Guid.NewGuid())
            {
                Modules = [new ConfiguredModule { ModuleId = moduleId }]
            };
            await using var sessionManager = CreateSessionManager(moduleId, telemetry, directory.FullName);

            await sessionManager.StartSessionAsync(preset, startSource: "schedule", scheduleId: scheduleId);
            await sessionManager.StopCurrentSessionAsync();
            using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await telemetry.FlushAsync(flushCts.Token);

            var events = ReadEvents(server).Where(item => item.GetProperty("event").GetString() is
                "SessionStarted" or "SessionStopped").ToArray();

            Assert.Equal(2, events.Length);
            var instanceIds = events.Select(item => item.GetProperty("properties")
                .GetProperty("sessionInstanceId").GetString()).Distinct().ToArray();
            Assert.Single(instanceIds);
            Assert.All(events, item => Assert.Equal(scheduleId.ToString("D"),
                item.GetProperty("properties").GetProperty("scheduleId").GetString()));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ZeroDurationAutoStop_EndsTheRunningSession()
    {
        await using var server = new PostHogTestServer();
        var directory = Directory.CreateTempSubdirectory("axorith-zero-autostop-telemetry-");
        try
        {
            await using var telemetry = CreateTelemetry(server, Guid.NewGuid().ToString("D"));
            var moduleId = Guid.NewGuid();
            var preset = new SessionPreset(Guid.NewGuid())
            {
                Modules = [new ConfiguredModule { ModuleId = moduleId }]
            };
            await using var sessionManager = CreateSessionManager(moduleId, telemetry, directory.FullName);
            await using var autoStop = new SessionAutoStopService(sessionManager,
                new InMemoryPresetManager(), new NoopNotifier(),
                NullLogger<SessionAutoStopService>.Instance, telemetry);
            var stopped = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
            sessionManager.SessionStopped += presetId => stopped.TrySetResult(presetId);

            await sessionManager.StartSessionAsync(preset);
            var sessionInstanceId = sessionManager.CurrentSessionInstanceId!.Value;
            await autoStop.StartAsync(CancellationToken.None);
            await autoStop.StartTrackingAsync(sessionInstanceId, TimeSpan.Zero, null);
            Assert.Equal(preset.Id, await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(sessionManager.IsSessionRunning);

            await telemetry.FlushAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
            var sessionStopped = ReadEvents(server).Single(item => item.GetProperty("event").GetString() == "SessionStopped")
                .GetProperty("properties");
            Assert.Equal(sessionInstanceId.ToString("D"), sessionStopped.GetProperty("sessionInstanceId").GetString());
            Assert.Equal("natural_completion", sessionStopped.GetProperty("stopReason").GetString());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task StopScheduleAndChainedSession_ShareTheRealSessionInstanceId()
    {
        await using var server = new PostHogTestServer();
        var directory = Directory.CreateTempSubdirectory("axorith-chained-session-telemetry-");
        try
        {
            await using var telemetry = CreateTelemetry(server, Guid.NewGuid().ToString("D"));
            var moduleId = Guid.NewGuid();
            var previousSessionInstanceId = Guid.NewGuid();
            var nextPreset = new SessionPreset(Guid.NewGuid())
            {
                Name = "Next",
                Modules = [new ConfiguredModule { ModuleId = moduleId }]
            };
            var currentPreset = new SessionPreset(Guid.NewGuid())
            {
                Name = "Current",
                Modules = [new ConfiguredModule { ModuleId = moduleId }]
            };
            var schedule = new SessionSchedule
            {
                Id = Guid.NewGuid(),
                PresetId = currentPreset.Id,
                Type = ScheduleType.StopRecurring,
                RecurringTime = TimeSpan.FromHours(18),
                NextPresetId = nextPreset.Id
            };
            await using var sessionManager = CreateSessionManager(moduleId, telemetry, directory.FullName);
            var presetManager = new InMemoryPresetManager(nextPreset);
            await using var autoStop = new SessionAutoStopService(sessionManager, presetManager,
                new NoopNotifier(), NullLogger<SessionAutoStopService>.Instance, telemetry);
            await autoStop.StartAsync(CancellationToken.None);

            await sessionManager.StartSessionAsync(currentPreset, sessionInstanceId: previousSessionInstanceId);
            var activePreset = sessionManager.ActiveSession!;
            await autoStop.StartTrackingAsync(previousSessionInstanceId, null, nextPreset.Id, schedule: schedule);
            Assert.True(await autoStop.CompleteNaturallyAsync(activePreset, nextPreset.Id, schedule: schedule));
            Assert.Equal(nextPreset.Id, sessionManager.ActiveSession?.Id);
            Assert.NotEqual(previousSessionInstanceId, sessionManager.CurrentSessionInstanceId);
            await sessionManager.StopCurrentSessionAsync();

            using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await telemetry.FlushAsync(flushCts.Token);

            var events = ReadEvents(server).ToArray();
            var stopTrigger = events.Single(item => item.GetProperty("event").GetString() == "ScheduleTriggered")
                .GetProperty("properties");
            var previousStopped = events.Single(item => item.GetProperty("event").GetString() == "SessionStopped" &&
                item.GetProperty("properties").GetProperty("presetId").GetString() == currentPreset.Id.ToString("D"))
                .GetProperty("properties");
            var chainedStarted = events.Single(item => item.GetProperty("event").GetString() == "SessionStarted" &&
                item.GetProperty("properties").GetProperty("presetId").GetString() == nextPreset.Id.ToString("D"))
                .GetProperty("properties");
            var chainedStopped = events.Single(item => item.GetProperty("event").GetString() == "SessionStopped" &&
                item.GetProperty("properties").GetProperty("presetId").GetString() == nextPreset.Id.ToString("D"))
                .GetProperty("properties");

            Assert.Equal(previousSessionInstanceId.ToString("D"),
                stopTrigger.GetProperty("sessionInstanceId").GetString());
            Assert.Equal(previousSessionInstanceId.ToString("D"),
                previousStopped.GetProperty("sessionInstanceId").GetString());
            Assert.Equal(previousSessionInstanceId.ToString("D"),
                chainedStarted.GetProperty("previousSessionInstanceId").GetString());
            Assert.Equal(previousSessionInstanceId.ToString("D"),
                chainedStopped.GetProperty("previousSessionInstanceId").GetString());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task FailedBreakStart_EmitsFailureTelemetryAfterRollingBack()
    {
        await using var server = new PostHogTestServer();
        var directory = Directory.CreateTempSubdirectory("axorith-break-failure-telemetry-");
        try
        {
            await using var telemetry = CreateTelemetry(server, Guid.NewGuid().ToString("D"));
            var moduleId = Guid.NewGuid();
            await using var sessionManager = CreateSessionManager(moduleId, telemetry, directory.FullName,
                failBreakStart: true);
            var preset = new SessionPreset(Guid.NewGuid())
            {
                Modules = [new ConfiguredModule { ModuleId = moduleId }],
                FocusCommitment = new FocusCommitmentOptions
                {
                    Mode = FocusCommitmentMode.Locked,
                    EndCondition = FocusEndCondition.Duration,
                    Duration = TimeSpan.FromMinutes(20),
                    BreakCount = 1,
                    BreakDuration = TimeSpan.FromMinutes(1)
                }
            };

            await sessionManager.StartSessionAsync(preset);
            var sessionInstanceId = sessionManager.CurrentSessionInstanceId!.Value;
            await Assert.ThrowsAsync<InvalidOperationException>(() => sessionManager.StartBreakAsync());
            Assert.Null(sessionManager.BreakEndsAt);
            Assert.Equal(1, sessionManager.BreaksRemaining);
            Assert.True(await sessionManager.EndCommittedSessionAsync(SessionEndReason.NaturalCompletion));

            using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await telemetry.FlushAsync(flushCts.Token);

            var events = ReadEvents(server).ToArray();
            Assert.DoesNotContain(events, item => item.GetProperty("event").GetString() == "SessionBreakStarted");
            var failed = events.Single(item => item.GetProperty("event").GetString() == "SessionBreakStartFailed")
                .GetProperty("properties");
            Assert.Equal("failed", failed.GetProperty("result").GetString());
            Assert.Equal("unknown", failed.GetProperty("failureReason").GetString());
            Assert.Equal(sessionInstanceId.ToString("D"), failed.GetProperty("sessionInstanceId").GetString());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ValidationWarning_IsReportedWithModuleContextAndNormalizedReason()
    {
        await using var server = new PostHogTestServer();
        var directory = Directory.CreateTempSubdirectory("axorith-validation-warning-telemetry-");
        try
        {
            await using var telemetry = CreateTelemetry(server, Guid.NewGuid().ToString("D"));
            var moduleId = Guid.NewGuid();
            var moduleInstanceId = Guid.NewGuid();
            var sessionInstanceId = Guid.NewGuid();
            const string privateWarning = "Warning with a private path C:\\private\\config";
            await using var sessionManager = CreateSessionManager(moduleId, telemetry, directory.FullName,
                validationResult: ValidationResult.Warn(privateWarning));
            var preset = new SessionPreset(Guid.NewGuid())
            {
                Modules = [new ConfiguredModule { ModuleId = moduleId, InstanceId = moduleInstanceId }]
            };

            await sessionManager.StartSessionAsync(preset, sessionInstanceId: sessionInstanceId);
            await sessionManager.StopCurrentSessionAsync();
            await telemetry.FlushAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

            var warning = ReadEvents(server).Single(item =>
                item.GetProperty("event").GetString() == "SessionValidationWarning").GetProperty("properties");
            Assert.Equal(sessionInstanceId.ToString("D"), warning.GetProperty("sessionInstanceId").GetString());
            Assert.Equal(moduleId.ToString("D"), warning.GetProperty("moduleId").GetString());
            Assert.Equal(moduleInstanceId.ToString("D"), warning.GetProperty("instanceId").GetString());
            Assert.Equal("custom", warning.GetProperty("moduleName").GetString());
            Assert.Equal("module_validation", warning.GetProperty("stage").GetString());
            Assert.Equal("warning", warning.GetProperty("result").GetString());
            Assert.Equal("validation_warning", warning.GetProperty("failureReason").GetString());
            Assert.DoesNotContain(privateWarning, string.Join("\n", server.Payloads), StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LockedSession_ReportsIgnoredDurationScheduleAsSkipped()
    {
        await using var server = new PostHogTestServer();
        var directory = Directory.CreateTempSubdirectory("axorith-committed-schedule-telemetry-");
        try
        {
            await using var telemetry = CreateTelemetry(server, Guid.NewGuid().ToString("D"));

            var presetId = Guid.NewGuid();
            var moduleId = Guid.NewGuid();
            var preset = new SessionPreset(presetId)
            {
                Name = "private preset name",
                Modules = [new ConfiguredModule { ModuleId = moduleId }],
                FocusCommitment = new FocusCommitmentOptions
                {
                    Mode = FocusCommitmentMode.Locked,
                    EndCondition = FocusEndCondition.Duration,
                    Duration = TimeSpan.FromMinutes(30)
                }
            };
            await using var sessionManager = CreateSessionManager(moduleId, telemetry, directory.FullName);

            var autoStop = new RecordingAutoStopService();
            var notifier = new NoopNotifier();
            var presetManager = new PresetManager(Path.Combine(directory.FullName, "presets"),
                NullLogger<PresetManager>.Instance);
            await using var scheduler = new ScheduleManager(directory.FullName, sessionManager, presetManager,
                autoStop, notifier, NullLogger<ScheduleManager>.Instance, telemetry);
            await scheduler.StartAsync(CancellationToken.None);
            await scheduler.SaveScheduleAsync(new SessionSchedule
            {
                Id = Guid.NewGuid(),
                PresetId = preset.Id,
                Name = "private schedule name",
                Type = ScheduleType.StopDuration,
                AutoStopDuration = TimeSpan.FromMinutes(5)
            }, CancellationToken.None);

            await sessionManager.StartSessionAsync(preset);
            await autoStop.TrackingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(await sessionManager.EndCommittedSessionAsync(SessionEndReason.NaturalCompletion));

            using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await telemetry.FlushAsync(flushCts.Token);

            var events = ReadEvents(server).ToArray();
            var skipped = events.Single(item => item.GetProperty("event").GetString() == "ScheduleTriggered")
                .GetProperty("properties");
            Assert.Equal("duration_stop", skipped.GetProperty("scheduleType").GetString());
            Assert.Equal("stop", skipped.GetProperty("triggerAction").GetString());
            Assert.Equal("skipped", skipped.GetProperty("result").GetString());
            Assert.Equal("committed_session", skipped.GetProperty("skipReason").GetString());
            var payloadText = string.Join("\n", server.Payloads);
            Assert.DoesNotContain("private preset name", payloadText, StringComparison.Ordinal);
            Assert.DoesNotContain("private schedule name", payloadText, StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static TelemetryService CreateTelemetry(PostHogTestServer server, string distinctId) =>
        new(new TelemetrySettings
        {
            Enabled = true,
            PostHogApiKey = "test-project-key",
            PostHogHost = server.Host,
            DistinctId = distinctId,
            ApplicationName = "Axorith.Host",
            AppVersion = "1.2.3",
            OsVersion = "Windows 11",
            BatchSize = 100,
            FlushInterval = TimeSpan.FromHours(1)
        });

    private static SessionManager CreateSessionManager(Guid moduleId, ITelemetryService telemetry, string directory,
        bool failBreakStart = false, ValidationResult? validationResult = null) =>
        new(new TestModuleRegistry(moduleId, failBreakStart, validationResult), NullLogger<SessionManager>.Instance, TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), telemetry,
            Path.Combine(directory, "committed-session.json"));

    private static IEnumerable<JsonElement> ReadEvents(PostHogTestServer server) => server.Payloads.SelectMany(body =>
    {
        using var payload = JsonDocument.Parse(body);
        return payload.RootElement.GetProperty("batch").EnumerateArray().Select(item => item.Clone()).ToArray();
    });

    private sealed class TestModuleRegistry(Guid moduleId, bool failBreakStart = false,
        ValidationResult? validationResult = null) : IModuleRegistry
    {
        private readonly ModuleDefinition _definition = new() { Id = moduleId, Name = "Test" };

        public IReadOnlyList<ModuleDefinition> GetAllDefinitions() => [_definition];
        public ModuleDefinition? GetDefinitionById(Guid id) => id == moduleId ? _definition : null;

        public (IModule? Instance, ILifetimeScope? Scope) CreateInstance(Guid id)
        {
            if (id != moduleId) return (null, null);

            var builder = new ContainerBuilder();
            builder.RegisterInstance(_definition).As<ModuleDefinition>();
            return (new TestModule(failBreakStart, validationResult), builder.Build());
        }
    }

    private sealed class TestModule(bool failBreakStart = false, ValidationResult? validationResult = null)
        : IModule, ISessionBreakParticipant
    {
        public IReadOnlyList<Axorith.Sdk.Settings.ISetting> GetSettings() => [];
        public IReadOnlyList<Axorith.Sdk.Actions.IAction> GetActions() => [];
        public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(validationResult ?? ValidationResult.Success);
        public Task OnSessionStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OnSessionEndAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PauseForBreakAsync(CancellationToken cancellationToken) => failBreakStart
            ? Task.FromException(new InvalidOperationException("test break pause failure"))
            : Task.CompletedTask;
        public Task ResumeAfterBreakAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
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

    private sealed class RecordingAutoStopService : ISessionAutoStopService
    {
        public TaskCompletionSource<bool> TrackingStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StartTrackingAsync(Guid sessionId, TimeSpan? autoStopDuration, Guid? nextPresetId,
            CancellationToken cancellationToken = default, SessionSchedule? schedule = null)
        {
            TrackingStarted.TrySetResult(true);
            return Task.CompletedTask;
        }

        public Task StopTrackingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public TimeSpan? GetTimeRemaining() => null;
        public Task ExecuteAfterEndActionAsync(AfterEndBehavior behavior) => Task.CompletedTask;
        public Task<bool> CompleteNaturallyAsync(SessionPreset expectedSession, Guid? fallbackNextPresetId,
            CancellationToken cancellationToken = default, SessionSchedule? schedule = null) => Task.FromResult(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopNotifier : INotifier
    {
        public void ShowToast(string message, NotificationType type = NotificationType.Info) { }
        public Task ShowSystemAsync(string title, string message, TimeSpan? expiration = null) => Task.CompletedTask;
    }

}
