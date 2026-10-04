using Autofac;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Services;
using Axorith.Host.Streaming;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;
using Axorith.Shared.Exceptions;
using Axorith.Telemetry;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Action = Axorith.Sdk.Actions.Action;
using ConfiguredModule = Axorith.Core.Models.ConfiguredModule;
using FocusCommitmentMode = Axorith.Core.Models.FocusCommitmentMode;
using FocusCommitmentOptions = Axorith.Core.Models.FocusCommitmentOptions;
using FocusEndCondition = Axorith.Core.Models.FocusEndCondition;
using AfterEndBehavior = Axorith.Core.Models.AfterEndBehavior;
using ModuleDefinition = Axorith.Sdk.ModuleDefinition;
using NotificationType = Axorith.Sdk.Services.NotificationType;
using Setting = Axorith.Sdk.Settings.Setting;

namespace Axorith.Host.Tests.Services;

public sealed class CommittedModuleMutationTests
{
    [Fact]
    public async Task FailedStrictProtectionRestoreKeepsSignInRecoveryEnabled()
    {
        using var env = new TestEnvironment("axorith-recovery-test-");
        var protection = new TestCommitmentProtectionService
        {
            FailRestore = true,
            RecoveryStatePath = env.RecoveryPath
        };
        var sessionManager = env.CreateSessionManager(protection);
        TestBlockerModule.SessionEndCount = 0;

        try
        {
            await sessionManager.StartSessionAsync(new SessionPreset
            {
                Id = Guid.NewGuid(),
                Name = "Strict recovery test",
                FocusCommitment = new FocusCommitmentOptions
                {
                    Mode = FocusCommitmentMode.Strict,
                    EndCondition = FocusEndCondition.Duration,
                    Duration = TimeSpan.FromMinutes(10)
                },
                Modules = [new ConfiguredModule { InstanceId = Guid.NewGuid(), ModuleId = env.Definition.Id }]
            });

            File.Exists(env.RecoveryPath).Should().BeTrue();
            protection.RecoveryStateExistedWhenActivated.Should().BeFalse();
            protection.RecoveryStartupChanges.Should().Equal(true);
            protection.Calls.Should().Equal("startup:active", "enable");

            var exception = await Assert.ThrowsAsync<SessionException>(() =>
                sessionManager.EndCommittedSessionAsync(SessionEndReason.EmergencyUnlock));

            exception.Message.Should().Contain("Windows protection could not be restored");
            protection.RecoveryStartupChanges.Should().Equal(true);
            protection.Calls.Should().Equal("startup:active", "enable", "restore");
            sessionManager.IsSessionRunning.Should().BeTrue();
            File.Exists(env.RecoveryPath).Should().BeTrue();
            TestBlockerModule.SessionEndCount.Should().Be(0);
        }
        finally
        {
            await sessionManager.DisposeAsync();
        }
    }

    [Fact]
    public async Task TamperedCommittedRecoveryIsRejectedAndRetained()
    {
        using var env = new TestEnvironment("axorith-tamper-test-");
        var runningManager = env.CreateSessionManager();
        var recoveringManager = env.CreateSessionManager();

        try
        {
            await runningManager.StartSessionAsync(CreateCommittedPreset("Tamper test", TimeSpan.FromMinutes(10),
                env.Definition.Id));
            var envelope = JsonNode.Parse(await File.ReadAllTextAsync(env.RecoveryPath))!.AsObject();
            envelope["Payload"] = envelope["Payload"]!.GetValue<string>() + " ";
            await File.WriteAllTextAsync(env.RecoveryPath, envelope.ToJsonString());

            await Assert.ThrowsAsync<SessionException>(() => recoveringManager.RecoverCommittedSessionAsync());

            File.Exists(env.RecoveryPath).Should().BeTrue();
            runningManager.IsSessionRunning.Should().BeTrue();
        }
        finally
        {
            await runningManager.EndCommittedSessionAsync(SessionEndReason.EmergencyUnlock);
            await recoveringManager.DisposeAsync();
            await runningManager.DisposeAsync();
        }
    }

    [Fact]
    public async Task NaturalCompletionStartsNextWorkspaceButEmergencyUnlockDoesNot()
    {
        using var env = new TestEnvironment("axorith-after-end-test-");
        var presetManager = new PresetManager(Path.Combine(env.Directory.FullName, "presets"),
            NullLogger<PresetManager>.Instance);
        var nextWorkspaceId = Guid.NewGuid();
        await presetManager.SavePresetAsync(new SessionPreset
        {
            Id = nextWorkspaceId,
            Name = "Next Workspace",
            Modules = [new ConfiguredModule { InstanceId = Guid.NewGuid(), ModuleId = env.Definition.Id }]
        }, CancellationToken.None);

        var sessionManager = env.CreateSessionManager();
        var notifier = new TestNotifier();
        var autoStopService = new SessionAutoStopService(sessionManager, presetManager, notifier,
            NullLogger<SessionAutoStopService>.Instance);
        var scheduleManager = new ScheduleManager(env.Directory.FullName, sessionManager, presetManager,
            autoStopService, notifier, NullLogger<ScheduleManager>.Instance);

        try
        {
            await autoStopService.StartAsync(CancellationToken.None);
            await scheduleManager.StartAsync(CancellationToken.None);

            await sessionManager.StartSessionAsync(CreateCommittedPreset("Emergency test", TimeSpan.FromMinutes(10)));
            await sessionManager.EndCommittedSessionAsync(SessionEndReason.EmergencyUnlock);
            sessionManager.IsSessionRunning.Should().BeFalse();

            await sessionManager.StartSessionAsync(CreateCommittedPreset("Natural completion test",
                TimeSpan.FromSeconds(2)));
            var transitionDeadline = DateTimeOffset.UtcNow.AddSeconds(12);
            while (DateTimeOffset.UtcNow < transitionDeadline && sessionManager.ActiveSession?.Id != nextWorkspaceId)
            {
                await Task.Delay(100);
            }

            sessionManager.ActiveSession?.Id.Should().Be(nextWorkspaceId);
        }
        finally
        {
            await StopIfRunningAsync(sessionManager);
            await scheduleManager.DisposeAsync();
            await autoStopService.DisposeAsync();
            await sessionManager.DisposeAsync();
        }

        SessionPreset CreateCommittedPreset(string name, TimeSpan duration) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            FocusCommitment = new FocusCommitmentOptions
            {
                Mode = FocusCommitmentMode.Locked,
                EndCondition = FocusEndCondition.Duration,
                Duration = duration,
                AfterEnd = AfterEndBehavior.StartNextWorkspace,
                NextWorkspaceId = nextWorkspaceId
            },
            Modules = [new ConfiguredModule { InstanceId = Guid.NewGuid(), ModuleId = env.Definition.Id }]
        };
    }

    [Fact]
    public async Task DuplicateNaturalCompletionOnlyTransitionsTheExpectedSessionOnce()
    {
        using var env = new TestEnvironment("axorith-natural-end-race-");
        var presetManager = new PresetManager(Path.Combine(env.Directory.FullName, "presets"),
            NullLogger<PresetManager>.Instance);
        var nextWorkspaceId = Guid.NewGuid();
        await presetManager.SavePresetAsync(new SessionPreset
        {
            Id = nextWorkspaceId,
            Name = "Next Workspace",
            Modules = [new ConfiguredModule { InstanceId = Guid.NewGuid(), ModuleId = env.Definition.Id }]
        }, CancellationToken.None);

        var sessionManager = env.CreateSessionManager();
        var autoStopService = new SessionAutoStopService(sessionManager, presetManager, new TestNotifier(),
            NullLogger<SessionAutoStopService>.Instance);

        try
        {
            await sessionManager.StartSessionAsync(new SessionPreset
            {
                Id = Guid.NewGuid(),
                Name = "Current Workspace",
                FocusCommitment = new FocusCommitmentOptions
                {
                    Mode = FocusCommitmentMode.Locked,
                    EndCondition = FocusEndCondition.Duration,
                    Duration = TimeSpan.FromMinutes(1),
                    AfterEnd = AfterEndBehavior.StartNextWorkspace,
                    NextWorkspaceId = nextWorkspaceId
                },
                Modules = [new ConfiguredModule { InstanceId = Guid.NewGuid(), ModuleId = env.Definition.Id }]
            });

            var expectedSession = sessionManager.ActiveSession!;
            var completions = await Task.WhenAll(
                autoStopService.CompleteNaturallyAsync(expectedSession, null),
                autoStopService.CompleteNaturallyAsync(expectedSession, null));

            completions.Count(completed => completed).Should().Be(1);
            sessionManager.ActiveSession?.Id.Should().Be(nextWorkspaceId);
        }
        finally
        {
            await StopIfRunningAsync(sessionManager);
            await autoStopService.DisposeAsync();
            await sessionManager.DisposeAsync();
        }
    }

    [Fact]
    public async Task FailedBreakStartupDoesNotConsumeBreakBudget()
    {
        using var env = new TestEnvironment("axorith-break-rollback-test-");
        var sessionManager = env.CreateSessionManager();

        try
        {
            var instanceId = Guid.NewGuid();
            await sessionManager.StartSessionAsync(new SessionPreset
            {
                Id = Guid.NewGuid(),
                Name = "Break rollback test",
                FocusCommitment = new FocusCommitmentOptions
                {
                    Mode = FocusCommitmentMode.Locked,
                    EndCondition = FocusEndCondition.Duration,
                    Duration = TimeSpan.FromMinutes(10),
                    BreakCount = 1,
                    BreakDuration = TimeSpan.FromMinutes(1)
                },
                Modules = [new ConfiguredModule { InstanceId = instanceId, ModuleId = env.Definition.Id }]
            });
            var module = (TestBlockerModule)sessionManager.GetActiveModuleInstanceByInstanceId(instanceId)!;
            module.FailPauseForBreak = true;

            await Assert.ThrowsAsync<InvalidOperationException>(() => sessionManager.StartBreakAsync());

            sessionManager.BreaksRemaining.Should().Be(1);
            sessionManager.BreakEndsAt.Should().BeNull();
            module.ResumeAfterBreakCount.Should().Be(1);
            using var state = JsonDocument.Parse(CommittedSessionStateFile.ReadPayload(env.RecoveryPath));
            state.RootElement.GetProperty("BreaksUsed").GetInt32().Should().Be(0);
            state.RootElement.GetProperty("BreakEndsAt").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally
        {
            await StopIfRunningAsync(sessionManager);
            await sessionManager.DisposeAsync();
        }
    }

    [Fact]
    public async Task FailedRecoveryWriteKeepsPreviousCommittedSnapshot()
    {
        using var env = new TestEnvironment("axorith-recovery-write-test-");
        var sessionManager = env.CreateSessionManager();

        try
        {
            await sessionManager.StartSessionAsync(CreateCommittedPreset("Recovery write test", TimeSpan.FromMinutes(10),
                env.Definition.Id, breakCount: 1));
            using (File.Open(env.RecoveryPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await Assert.ThrowsAsync<SessionException>(() => sessionManager.StartBreakAsync());
            }

            sessionManager.BreaksRemaining.Should().Be(1);
            using var state = JsonDocument.Parse(CommittedSessionStateFile.ReadPayload(env.RecoveryPath));
            state.RootElement.GetProperty("BreaksUsed").GetInt32().Should().Be(0);
        }
        finally
        {
            await StopIfRunningAsync(sessionManager);
            await sessionManager.DisposeAsync();
        }
    }

    [Fact]
    public async Task DirectCommittedStartRunsPreflightBeforeModuleSideEffects()
    {
        using var env = new TestEnvironment("axorith-committed-preflight-test-");
        var sessionManager = env.CreateSessionManager();
        TestBlockerModule.SessionStartCount = 0;

        try
        {
            var preset = CreateCommittedPreset("Preflight test", TimeSpan.FromMinutes(10), env.Definition.Id);
            preset.Modules[0].Settings["BlockRule"] = "unavailable";

            await Assert.ThrowsAsync<SessionException>(() => sessionManager.StartSessionAsync(preset));

            TestBlockerModule.SessionStartCount.Should().Be(0);
            sessionManager.IsSessionRunning.Should().BeFalse();
            File.Exists(env.RecoveryPath).Should().BeFalse();
        }
        finally
        {
            await sessionManager.DisposeAsync();
        }
    }

    [Fact]
    public async Task LockedSessionRejectsLiveSettingAndBlockerActionMutations()
    {
        using var env = new TestEnvironment("axorith-committed-test-");
        var presetManager = new PresetManager(Path.Combine(env.Directory.FullName, "presets"),
            NullLogger<PresetManager>.Instance);
        var nextWorkspaceId = Guid.NewGuid();
        await presetManager.SavePresetAsync(new SessionPreset
        {
            Id = nextWorkspaceId,
            Name = "Next Workspace"
        }, CancellationToken.None);

        var sessionManager = env.CreateSessionManager();
        var broadcaster = new SettingUpdateBroadcaster(sessionManager,
            NullLogger<SettingUpdateBroadcaster>.Instance, Options.Create(new Configuration()));
        var sandboxManager = new DesignTimeSandboxManager(env.Registry, broadcaster,
            NullLogger<DesignTimeSandboxManager>.Instance, Options.Create(new Configuration()), sessionManager);
        var notifier = new TestNotifier();
        var autoStopService = new SessionAutoStopService(sessionManager, presetManager, notifier,
            NullLogger<SessionAutoStopService>.Instance);
        var scheduleManager = new ScheduleManager(env.Directory.FullName, sessionManager, presetManager,
            autoStopService, notifier, NullLogger<ScheduleManager>.Instance);
        var presetsService = new PresetsServiceImpl(presetManager, scheduleManager, sandboxManager, env.Registry,
            sessionManager, NullLogger<PresetsServiceImpl>.Instance);
        var service = new ModulesServiceImpl(env.Registry, sessionManager, broadcaster, sandboxManager,
            NullLogger<ModulesServiceImpl>.Instance);
        var instanceId = Guid.NewGuid();

        try
        {
            await sessionManager.StartSessionAsync(new SessionPreset
            {
                Id = Guid.NewGuid(),
                Name = "Locked test",
                FocusCommitment = new FocusCommitmentOptions
                {
                    Mode = FocusCommitmentMode.Locked,
                    EndCondition = FocusEndCondition.Duration,
                    Duration = TimeSpan.FromMinutes(10),
                    AfterEnd = AfterEndBehavior.StartNextWorkspace,
                    NextWorkspaceId = nextWorkspaceId
                },
                Modules = [new ConfiguredModule { InstanceId = instanceId, ModuleId = env.Definition.Id }]
            });

            var module = (TestBlockerModule)sessionManager.GetActiveModuleInstanceByInstanceId(instanceId)!;
            var settingResult = await service.UpdateSetting(new UpdateSettingRequest
            {
                ModuleInstanceId = instanceId.ToString(),
                SettingKey = "BlockRule",
                StringValue = "weakened"
            }, GrpcTestContext.Create());
            var actionResult = await service.InvokeAction(new InvokeActionRequest
            {
                ModuleInstanceId = instanceId.ToString(),
                ActionKey = "WeakenRule"
            }, GrpcTestContext.Create());

            settingResult.Success.Should().BeFalse();
            actionResult.Success.Should().BeFalse();
            module.BlockRule.GetCurrentValue().Should().Be("fixed");
            module.WeakenActionInvoked.Should().BeFalse();

            module.IsHealthy = false;
            await sessionManager.RefreshProtectionHealthAsync();
            sessionManager.ProtectionStatus.Should().StartWith("Protection degraded");

            module.IsHealthy = true;
            await sessionManager.RefreshProtectionHealthAsync();
            sessionManager.ProtectionStatus.Should().StartWith("Protection recovered");

            await sessionManager.RefreshProtectionHealthAsync();
            sessionManager.ProtectionStatus.Should().StartWith("Protection active");

            var updateException = await Assert.ThrowsAsync<RpcException>(() => presetsService.UpdatePreset(
                new UpdatePresetRequest
                {
                    Preset = PresetCodec.ToMessage(new SessionPreset
                    {
                        Id = nextWorkspaceId,
                        Name = "Changed next Workspace"
                    })
                }, GrpcTestContext.Create()));
            updateException.StatusCode.Should().Be(StatusCode.FailedPrecondition);

            var deleteException = await Assert.ThrowsAsync<RpcException>(() => presetsService.DeletePreset(
                new DeletePresetRequest { PresetId = nextWorkspaceId.ToString() }, GrpcTestContext.Create()));
            deleteException.StatusCode.Should().Be(StatusCode.FailedPrecondition);
            (await presetManager.GetPresetByIdAsync(nextWorkspaceId, CancellationToken.None))!.Name
                .Should().Be("Next Workspace");
        }
        finally
        {
            await sessionManager.EndCommittedSessionAsync(SessionEndReason.EmergencyUnlock);
            await sessionManager.DisposeAsync();
            broadcaster.Dispose();
            sandboxManager.Dispose();
        }
    }
    private static async Task StopIfRunningAsync(SessionManager sessionManager)
    {
        if (!sessionManager.IsSessionRunning)
            return;

        if (sessionManager.ActiveSession?.FocusCommitment.IsCommitted == true)
            await sessionManager.EndCommittedSessionAsync(SessionEndReason.EmergencyUnlock);
        else
            await sessionManager.StopCurrentSessionAsync();
    }

    private static SessionPreset CreateCommittedPreset(string name, TimeSpan duration, Guid moduleId,
        int breakCount = 0) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        FocusCommitment = new FocusCommitmentOptions
        {
            Mode = FocusCommitmentMode.Locked,
            EndCondition = FocusEndCondition.Duration,
            Duration = duration,
            BreakCount = breakCount,
            BreakDuration = TimeSpan.FromMinutes(1)
        },
        Modules = [new ConfiguredModule { InstanceId = Guid.NewGuid(), ModuleId = moduleId }]
    };

    private sealed class TestEnvironment : IDisposable
    {
        private readonly ILifetimeScope _rootScope = new ContainerBuilder().Build();

        public TestEnvironment(string prefix)
        {
            Definition = new ModuleDefinition
            {
                Id = Guid.NewGuid(),
                Name = "Test Blocker",
                ModuleType = typeof(TestBlockerModule)
            };
            Registry = new TestModuleRegistry(_rootScope, Definition);
            Directory = System.IO.Directory.CreateTempSubdirectory(prefix);
        }

        public ModuleDefinition Definition { get; }
        public TestModuleRegistry Registry { get; }
        public DirectoryInfo Directory { get; }
        public string RecoveryPath => Path.Combine(Directory.FullName, "committed-session.json");

        public SessionManager CreateSessionManager(ICommitmentProtectionService? protection = null) =>
            new(Registry, NullLogger<SessionManager>.Instance, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(10), NoopTelemetryService.Instance, RecoveryPath, protection);

        public void Dispose()
        {
            _rootScope.Dispose();
            Directory.Delete(recursive: true);
        }
    }

    private sealed class TestModuleRegistry(ILifetimeScope rootScope, ModuleDefinition definition) : IModuleRegistry
    {
        public IReadOnlyList<ModuleDefinition> GetAllDefinitions() => [definition];

        public ModuleDefinition? GetDefinitionById(Guid moduleId) => moduleId == definition.Id ? definition : null;

        public (IModule? Instance, ILifetimeScope? Scope) CreateInstance(Guid moduleId)
        {
            if (moduleId != definition.Id)
            {
                return (null, null);
            }

            var scope = rootScope.BeginLifetimeScope(builder =>
            {
                builder.RegisterInstance(definition).As<ModuleDefinition>();
                builder.RegisterType(definition.ModuleType!).As<IModule>();
            });
            return (scope.Resolve<IModule>(), scope);
        }
    }

    private sealed class TestBlockerModule : IModule, ICommittedSessionValidator, ISessionBreakParticipant
    {
        private readonly Action _weakenAction;
        public static int SessionStartCount { get; set; }
        public static int SessionEndCount { get; set; }

        public Setting<string> BlockRule { get; } = Setting.AsText("BlockRule", "Block rule", "fixed");
        public bool WeakenActionInvoked { get; private set; }
        public bool IsHealthy { get; set; } = true;
        public bool FailPauseForBreak { get; set; }
        public int ResumeAfterBreakCount { get; private set; }
        public bool IsProtectionDegraded => !IsHealthy;
        public string ProtectionStatusMessage => "Test blocker active";

        public TestBlockerModule()
        {
            _weakenAction = new Action("WeakenRule", "Weaken rule");
            _weakenAction.OnInvokeAsync(() =>
            {
                WeakenActionInvoked = true;
                BlockRule.SetValue("weakened");
                return Task.CompletedTask;
            });
        }

        public IReadOnlyList<ISetting> GetSettings() => [BlockRule];

        public IReadOnlyList<IAction> GetActions() => [_weakenAction];

        public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ValidationResult.Success);

        public Task OnSessionStartAsync(CancellationToken cancellationToken)
        {
            SessionStartCount++;
            return Task.CompletedTask;
        }

        public Task OnSessionEndAsync(CancellationToken cancellationToken)
        {
            SessionEndCount++;
            return Task.CompletedTask;
        }

        public Task<bool> CanStartCommittedSessionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(BlockRule.GetCurrentValue() != "unavailable");

        public Task<bool> IsProtectionHealthyAsync(CancellationToken cancellationToken) => Task.FromResult(IsHealthy);

        public Task PauseForBreakAsync(CancellationToken cancellationToken) => FailPauseForBreak
            ? Task.FromException(new InvalidOperationException("Test break pause failure"))
            : Task.CompletedTask;

        public Task ResumeAfterBreakAsync(CancellationToken cancellationToken)
        {
            ResumeAfterBreakCount++;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            BlockRule.Dispose();
            _weakenAction.Dispose();
        }
    }

    private sealed class TestNotifier : INotifier
    {
        public void ShowToast(string message, NotificationType type = NotificationType.Info)
        {
        }

        public Task ShowSystemAsync(string title, string message, TimeSpan? expiration = null) => Task.CompletedTask;
    }

    private sealed class TestCommitmentProtectionService : ICommitmentProtectionService
    {
        public bool FailRestore { get; init; }
        public string? RecoveryStatePath { get; init; }
        public bool? RecoveryStateExistedWhenActivated { get; private set; }
        public List<bool> RecoveryStartupChanges { get; } = [];
        public List<string> Calls { get; } = [];

        public Task CheckCanEnableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CheckRecoveryStateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CheckCanSetRecoveryStartupAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetRecoveryStartupAsync(bool active, CancellationToken cancellationToken = default)
        {
            RecoveryStartupChanges.Add(active);
            Calls.Add(active ? "startup:active" : "startup:inactive");
            if (active && RecoveryStatePath is { } path)
            {
                RecoveryStateExistedWhenActivated = File.Exists(path);
            }

            return Task.CompletedTask;
        }

        public Task EnableAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("enable");
            return Task.CompletedTask;
        }

        public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task RestoreAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("restore");
            return FailRestore
                ? Task.FromException(new InvalidOperationException("Test restore failure"))
                : Task.CompletedTask;
        }

        public Task ReconcileAsync(bool strictSessionActive, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
