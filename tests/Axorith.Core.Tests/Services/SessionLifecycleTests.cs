using Autofac;
using Axorith.Core.Models;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Axorith.Sdk.Services;
using Axorith.Shared.Exceptions;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Core.Tests.Services;

public sealed class SessionLifecycleTests
{
    [Fact]
    public async Task ManualStartRejectsDuplicateSession()
    {
        var directory = Directory.CreateTempSubdirectory("axorith-session-lifecycle-");
        try
        {
            var presetId = Guid.NewGuid();
            var moduleId = Guid.NewGuid();
            var sessionInstanceId = Guid.NewGuid();
            var preset = new SessionPreset(presetId)
            {
                Name = "Session test",
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

            await using var sessionManager = CreateSessionManager(moduleId, NoopTelemetryService.Instance, directory.FullName);

            await sessionManager.StartSessionAsync(preset, startSource: "manual", sessionInstanceId: sessionInstanceId);
            await Assert.ThrowsAsync<SessionException>(() =>
                sessionManager.StartSessionAsync(preset, startSource: "manual", sessionInstanceId: sessionInstanceId));
            Assert.Equal(sessionInstanceId, sessionManager.CurrentSessionInstanceId);
            Assert.Equal(presetId, sessionManager.ActiveSession?.Id);
            Assert.True(await sessionManager.EndCommittedSessionAsync(SessionEndReason.NaturalCompletion));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScheduledStartSkipsUnavailableModules()
    {
        var directory = Directory.CreateTempSubdirectory("axorith-scheduled-session-lifecycle-");
        try
        {
            var moduleId = Guid.NewGuid();
            var scheduleId = Guid.NewGuid();
            var unavailableModuleId = Guid.NewGuid();
            var preset = new SessionPreset(Guid.NewGuid())
            {
                Modules =
                [
                    new ConfiguredModule { ModuleId = moduleId },
                    new ConfiguredModule { ModuleId = unavailableModuleId }
                ]
            };
            await using var sessionManager = CreateSessionManager(moduleId, NoopTelemetryService.Instance, directory.FullName);

            await sessionManager.StartSessionAsync(preset, startSource: "schedule", scheduleId: scheduleId);
            Assert.Single(sessionManager.GetCurrentSnapshot()!.Modules);
            await sessionManager.StopCurrentSessionAsync();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ZeroDurationAutoStop_EndsTheRunningSession()
    {
        var directory = Directory.CreateTempSubdirectory("axorith-zero-autostop-lifecycle-");
        try
        {
            var moduleId = Guid.NewGuid();
            var preset = new SessionPreset(Guid.NewGuid())
            {
                Modules = [new ConfiguredModule { ModuleId = moduleId }]
            };
            await using var sessionManager = CreateSessionManager(moduleId, NoopTelemetryService.Instance, directory.FullName);
            await using var autoStop = new SessionAutoStopService(sessionManager,
                new InMemoryPresetManager(), new NoopNotifier(),
                NullLogger<SessionAutoStopService>.Instance, NoopTelemetryService.Instance);
            var stopped = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
            sessionManager.SessionStopped += presetId => stopped.TrySetResult(presetId);

            await sessionManager.StartSessionAsync(preset);
            var sessionInstanceId = sessionManager.CurrentSessionInstanceId!.Value;
            await autoStop.StartAsync(CancellationToken.None);
            await autoStop.StartTrackingAsync(sessionInstanceId, TimeSpan.Zero, null);
            Assert.Equal(preset.Id, await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(sessionManager.IsSessionRunning);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task NaturalCompletionStartsNextSessionWithANewInstanceId()
    {
        var directory = Directory.CreateTempSubdirectory("axorith-chained-session-lifecycle-");
        try
        {
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
            await using var sessionManager = CreateSessionManager(moduleId, NoopTelemetryService.Instance, directory.FullName);
            var presetManager = new InMemoryPresetManager(nextPreset);
            await using var autoStop = new SessionAutoStopService(sessionManager, presetManager,
                new NoopNotifier(), NullLogger<SessionAutoStopService>.Instance, NoopTelemetryService.Instance);
            await autoStop.StartAsync(CancellationToken.None);

            await sessionManager.StartSessionAsync(currentPreset, sessionInstanceId: previousSessionInstanceId);
            var activePreset = sessionManager.ActiveSession!;
            await autoStop.StartTrackingAsync(previousSessionInstanceId, null, nextPreset.Id, schedule: schedule);
            Assert.True(await autoStop.CompleteNaturallyAsync(activePreset, nextPreset.Id, schedule: schedule));
            Assert.Equal(nextPreset.Id, sessionManager.ActiveSession?.Id);
            Assert.NotEqual(previousSessionInstanceId, sessionManager.CurrentSessionInstanceId);
            await sessionManager.StopCurrentSessionAsync();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task BreakRoundtripConsumesExactlyOneBreak()
    {
        var directory = Directory.CreateTempSubdirectory("axorith-break-lifecycle-");
        try
        {
            var moduleId = Guid.NewGuid();
            await using var manager = CreateSessionManager(moduleId, NoopTelemetryService.Instance, directory.FullName);
            var preset = new SessionPreset(Guid.NewGuid())
            {
                Modules = [new ConfiguredModule { ModuleId = moduleId }],
                FocusCommitment = new FocusCommitmentOptions
                {
                    Mode = FocusCommitmentMode.Locked,
                    EndCondition = FocusEndCondition.Duration,
                    Duration = TimeSpan.FromMinutes(20),
                    BreakCount = 2,
                    BreakDuration = TimeSpan.FromMinutes(1)
                }
            };
            await manager.StartSessionAsync(preset);
            await manager.StartBreakAsync();
            Assert.NotNull(manager.BreakEndsAt);
            Assert.Equal(1, manager.BreaksRemaining);
            await manager.EndBreakAsync();
            Assert.Null(manager.BreakEndsAt);
            Assert.Equal(1, manager.BreaksRemaining);
            Assert.True(await manager.EndCommittedSessionAsync(SessionEndReason.NaturalCompletion));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task FailedBreakStartRestoresBreakBudget()
    {
        var directory = Directory.CreateTempSubdirectory("axorith-break-failure-lifecycle-");
        try
        {
            var moduleId = Guid.NewGuid();
            await using var sessionManager = CreateSessionManager(moduleId, NoopTelemetryService.Instance, directory.FullName,
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
            await Assert.ThrowsAsync<InvalidOperationException>(() => sessionManager.StartBreakAsync());
            Assert.Null(sessionManager.BreakEndsAt);
            Assert.Equal(1, sessionManager.BreaksRemaining);
            Assert.True(await sessionManager.EndCommittedSessionAsync(SessionEndReason.NaturalCompletion));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ValidationWarningAllowsSessionToStart()
    {
        var directory = Directory.CreateTempSubdirectory("axorith-validation-warning-lifecycle-");
        try
        {
            var moduleId = Guid.NewGuid();
            var moduleInstanceId = Guid.NewGuid();
            var sessionInstanceId = Guid.NewGuid();
            const string privateWarning = "Warning with a private path C:\\private\\config";
            await using var sessionManager = CreateSessionManager(moduleId, NoopTelemetryService.Instance, directory.FullName,
                validationResult: ValidationResult.Warn(privateWarning));
            var preset = new SessionPreset(Guid.NewGuid())
            {
                Modules = [new ConfiguredModule { ModuleId = moduleId, InstanceId = moduleInstanceId }]
            };

            await sessionManager.StartSessionAsync(preset, sessionInstanceId: sessionInstanceId);
            Assert.True(sessionManager.IsSessionRunning);
            Assert.Equal(sessionInstanceId, sessionManager.CurrentSessionInstanceId);
            await sessionManager.StopCurrentSessionAsync();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LockedSessionIgnoresExternalDurationSchedule()
    {
        var directory = Directory.CreateTempSubdirectory("axorith-committed-schedule-lifecycle-");
        try
        {
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
            await using var sessionManager = CreateSessionManager(moduleId, NoopTelemetryService.Instance, directory.FullName);

            var autoStop = new RecordingAutoStopService();
            var notifier = new NoopNotifier();
            var presetManager = new PresetManager(Path.Combine(directory.FullName, "presets"),
                NullLogger<PresetManager>.Instance);
            await using var scheduler = new ScheduleManager(directory.FullName, sessionManager, presetManager,
                autoStop, notifier, NullLogger<ScheduleManager>.Instance, NoopTelemetryService.Instance);
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
            Assert.InRange(autoStop.Duration!.Value, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(30));
            Assert.Null(autoStop.Schedule);
            Assert.Equal(sessionManager.CurrentSessionInstanceId, autoStop.SessionId);
            Assert.True(sessionManager.IsSessionRunning);
            Assert.True(await sessionManager.EndCommittedSessionAsync(SessionEndReason.NaturalCompletion));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static SessionManager CreateSessionManager(Guid moduleId, ITelemetryService telemetry, string directory,
        bool failBreakStart = false, ValidationResult? validationResult = null) =>
        new(new TestModuleRegistry(moduleId, failBreakStart, validationResult), NullLogger<SessionManager>.Instance, TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), telemetry,
            Path.Combine(directory, "committed-session.json"));

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
            builder.Register(_ => new TestModule(failBreakStart, validationResult)).As<IModule>();
            var scope = builder.Build();
            return (scope.Resolve<IModule>(), scope);
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
            Task.FromResult<IReadOnlyList<SessionPreset>>([.. _presets.Values]);

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
        public TimeSpan? Duration { get; private set; }
        public Guid SessionId { get; private set; }
        public SessionSchedule? Schedule { get; private set; }

        public TaskCompletionSource<bool> TrackingStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StartTrackingAsync(Guid sessionId, TimeSpan? autoStopDuration, Guid? nextPresetId,
            CancellationToken cancellationToken = default, SessionSchedule? schedule = null)
        {
            Duration = autoStopDuration;
            SessionId = sessionId;
            Schedule = schedule;
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
