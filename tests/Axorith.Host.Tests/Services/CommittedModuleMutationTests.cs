using Autofac;
using System.Text.Json;
using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Mappers;
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
using Grpc.Core.Testing;
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
        using var rootScope = new ContainerBuilder().Build();
        var definition = new ModuleDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Test Blocker",
            ModuleType = typeof(TestBlockerModule)
        };
        var registry = new TestModuleRegistry(rootScope, definition);
        var recoveryDirectory = Directory.CreateTempSubdirectory("axorith-recovery-test-");
        var recoveryPath = Path.Combine(recoveryDirectory.FullName, "committed-session.json");
        var protection = new TestCommitmentProtectionService
        {
            FailRestore = true,
            RecoveryStatePath = recoveryPath
        };
        var sessionManager = new SessionManager(registry, NullLogger<SessionManager>.Instance,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10),
            new NoopTelemetryService(), recoveryPath, protection);

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
                Modules = [new ConfiguredModule { InstanceId = Guid.NewGuid(), ModuleId = definition.Id }]
            });

            File.Exists(recoveryPath).Should().BeTrue();
            protection.RecoveryStateExistedWhenActivated.Should().BeFalse();
            protection.RecoveryStartupChanges.Should().Equal(true);
            protection.Calls.Should().Equal("startup:active", "enable");

            var exception = await Assert.ThrowsAsync<SessionException>(() =>
                sessionManager.EndCommittedSessionAsync(SessionEndReason.EmergencyUnlock));

            exception.Message.Should().Contain("Windows protection could not be restored");
            protection.RecoveryStartupChanges.Should().Equal(true);
            protection.Calls.Should().Equal("startup:active", "enable", "restore");
            sessionManager.IsSessionRunning.Should().BeFalse();
        }
        finally
        {
            await sessionManager.DisposeAsync();
            recoveryDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task NaturalCompletionStartsNextWorkspaceButEmergencyUnlockDoesNot()
    {
        using var rootScope = new ContainerBuilder().Build();
        var definition = new ModuleDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Test Blocker",
            ModuleType = typeof(TestBlockerModule)
        };
        var registry = new TestModuleRegistry(rootScope, definition);
        var recoveryDirectory = Directory.CreateTempSubdirectory("axorith-after-end-test-");
        var presetManager = new PresetManager(Path.Combine(recoveryDirectory.FullName, "presets"),
            NullLogger<PresetManager>.Instance);
        var nextWorkspaceId = Guid.NewGuid();
        await presetManager.SavePresetAsync(new SessionPreset
        {
            Id = nextWorkspaceId,
            Name = "Next Workspace",
            Modules = [new ConfiguredModule { InstanceId = Guid.NewGuid(), ModuleId = definition.Id }]
        }, CancellationToken.None);

        var sessionManager = new SessionManager(registry, NullLogger<SessionManager>.Instance,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10),
            new NoopTelemetryService(), Path.Combine(recoveryDirectory.FullName, "committed-session.json"));
        var notifier = new TestNotifier();
        var autoStopService = new SessionAutoStopService(sessionManager, presetManager, notifier,
            NullLogger<SessionAutoStopService>.Instance);
        var scheduleManager = new ScheduleManager(recoveryDirectory.FullName, sessionManager, presetManager,
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
            if (sessionManager.ActiveSession?.FocusCommitment.Mode is FocusCommitmentMode.Locked or
                FocusCommitmentMode.Strict)
            {
                await sessionManager.EndCommittedSessionAsync(SessionEndReason.EmergencyUnlock);
            }
            else if (sessionManager.IsSessionRunning)
            {
                await sessionManager.StopCurrentSessionAsync();
            }

            await scheduleManager.DisposeAsync();
            await autoStopService.DisposeAsync();
            await sessionManager.DisposeAsync();
            recoveryDirectory.Delete(recursive: true);
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
            Modules = [new ConfiguredModule { InstanceId = Guid.NewGuid(), ModuleId = definition.Id }]
        };
    }

    [Fact]
    public async Task DuplicateNaturalCompletionOnlyTransitionsTheExpectedSessionOnce()
    {
        using var rootScope = new ContainerBuilder().Build();
        var definition = new ModuleDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Test Blocker",
            ModuleType = typeof(TestBlockerModule)
        };
        var registry = new TestModuleRegistry(rootScope, definition);
        var recoveryDirectory = Directory.CreateTempSubdirectory("axorith-natural-end-race-");
        var presetManager = new PresetManager(Path.Combine(recoveryDirectory.FullName, "presets"),
            NullLogger<PresetManager>.Instance);
        var nextWorkspaceId = Guid.NewGuid();
        await presetManager.SavePresetAsync(new SessionPreset
        {
            Id = nextWorkspaceId,
            Name = "Next Workspace",
            Modules = [new ConfiguredModule { InstanceId = Guid.NewGuid(), ModuleId = definition.Id }]
        }, CancellationToken.None);

        var sessionManager = new SessionManager(registry, NullLogger<SessionManager>.Instance,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10),
            new NoopTelemetryService(), Path.Combine(recoveryDirectory.FullName, "committed-session.json"));
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
                Modules = [new ConfiguredModule { InstanceId = Guid.NewGuid(), ModuleId = definition.Id }]
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
            if (sessionManager.ActiveSession?.FocusCommitment.Mode is FocusCommitmentMode.Locked or
                FocusCommitmentMode.Strict)
            {
                await sessionManager.EndCommittedSessionAsync(SessionEndReason.EmergencyUnlock);
            }
            else if (sessionManager.IsSessionRunning)
            {
                await sessionManager.StopCurrentSessionAsync();
            }

            await autoStopService.DisposeAsync();
            await sessionManager.DisposeAsync();
            recoveryDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task FailedBreakStartupDoesNotConsumeBreakBudget()
    {
        using var rootScope = new ContainerBuilder().Build();
        var definition = new ModuleDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Test Blocker",
            ModuleType = typeof(TestBlockerModule)
        };
        var registry = new TestModuleRegistry(rootScope, definition);
        var recoveryDirectory = Directory.CreateTempSubdirectory("axorith-break-rollback-test-");
        var recoveryPath = Path.Combine(recoveryDirectory.FullName, "committed-session.json");
        var sessionManager = new SessionManager(registry, NullLogger<SessionManager>.Instance,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10),
            new NoopTelemetryService(), recoveryPath);

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
                Modules = [new ConfiguredModule { InstanceId = instanceId, ModuleId = definition.Id }]
            });
            var module = (TestBlockerModule)sessionManager.GetActiveModuleInstanceByInstanceId(instanceId)!;
            module.FailPauseForBreak = true;

            await Assert.ThrowsAsync<InvalidOperationException>(() => sessionManager.StartBreakAsync());

            sessionManager.BreaksRemaining.Should().Be(1);
            sessionManager.BreakEndsAt.Should().BeNull();
            module.ResumeAfterBreakCount.Should().Be(1);
            using var state = JsonDocument.Parse(await File.ReadAllTextAsync(recoveryPath));
            state.RootElement.GetProperty("BreaksUsed").GetInt32().Should().Be(0);
            state.RootElement.GetProperty("BreakEndsAt").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally
        {
            if (sessionManager.IsSessionRunning)
            {
                await sessionManager.EndCommittedSessionAsync(SessionEndReason.EmergencyUnlock);
            }

            await sessionManager.DisposeAsync();
            recoveryDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LockedSessionRejectsLiveSettingAndBlockerActionMutations()
    {
        using var rootScope = new ContainerBuilder().Build();
        var definition = new ModuleDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Test Blocker",
            ModuleType = typeof(TestBlockerModule)
        };
        var registry = new TestModuleRegistry(rootScope, definition);
        var recoveryDirectory = Directory.CreateTempSubdirectory("axorith-committed-test-");
        var presetManager = new PresetManager(Path.Combine(recoveryDirectory.FullName, "presets"),
            NullLogger<PresetManager>.Instance);
        var nextWorkspaceId = Guid.NewGuid();
        await presetManager.SavePresetAsync(new SessionPreset
        {
            Id = nextWorkspaceId,
            Name = "Next Workspace"
        }, CancellationToken.None);

        var sessionManager = new SessionManager(registry, NullLogger<SessionManager>.Instance,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10),
            new NoopTelemetryService(), Path.Combine(recoveryDirectory.FullName, "committed-session.json"));
        var broadcaster = new SettingUpdateBroadcaster(sessionManager,
            NullLogger<SettingUpdateBroadcaster>.Instance, Options.Create(new Configuration()));
        var sandboxManager = new DesignTimeSandboxManager(registry, broadcaster,
            NullLogger<DesignTimeSandboxManager>.Instance, Options.Create(new Configuration()), sessionManager);
        var notifier = new TestNotifier();
        var autoStopService = new SessionAutoStopService(sessionManager, presetManager, notifier,
            NullLogger<SessionAutoStopService>.Instance);
        var scheduleManager = new ScheduleManager(recoveryDirectory.FullName, sessionManager, presetManager,
            autoStopService, notifier, NullLogger<ScheduleManager>.Instance);
        var presetsService = new PresetsServiceImpl(presetManager, scheduleManager, sandboxManager, registry,
            sessionManager, NullLogger<PresetsServiceImpl>.Instance);
        var service = new ModulesServiceImpl(registry, sessionManager, broadcaster, sandboxManager,
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
                Modules = [new ConfiguredModule { InstanceId = instanceId, ModuleId = definition.Id }]
            });

            var module = (TestBlockerModule)sessionManager.GetActiveModuleInstanceByInstanceId(instanceId)!;
            var settingResult = await service.UpdateSetting(new UpdateSettingRequest
            {
                ModuleInstanceId = instanceId.ToString(),
                SettingKey = "BlockRule",
                StringValue = "weakened"
            }, CreateTestContext());
            var actionResult = await service.InvokeAction(new InvokeActionRequest
            {
                ModuleInstanceId = instanceId.ToString(),
                ActionKey = "WeakenRule"
            }, CreateTestContext());

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
                    Preset = PresetMapper.ToMessage(new SessionPreset
                    {
                        Id = nextWorkspaceId,
                        Name = "Changed next Workspace"
                    })
                }, CreateTestContext()));
            updateException.StatusCode.Should().Be(StatusCode.FailedPrecondition);

            var deleteException = await Assert.ThrowsAsync<RpcException>(() => presetsService.DeletePreset(
                new DeletePresetRequest { PresetId = nextWorkspaceId.ToString() }, CreateTestContext()));
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
            recoveryDirectory.Delete(recursive: true);
        }
    }

    private static ServerCallContext CreateTestContext() => TestServerCallContext.Create(
        method: "TestMethod",
        host: "localhost",
        deadline: DateTime.UtcNow.AddMinutes(5),
        requestHeaders: [],
        cancellationToken: CancellationToken.None,
        peer: "127.0.0.1",
        authContext: null,
        contextPropagationToken: null,
        writeHeadersFunc: _ => Task.CompletedTask,
        writeOptionsGetter: () => new WriteOptions(),
        writeOptionsSetter: _ => { });

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

        public Setting<string> BlockRule { get; } = Setting.AsText("BlockRule", "Block rule", "fixed");
        public bool WeakenActionInvoked { get; private set; }
        public bool IsHealthy { get; set; } = true;
        public bool FailPauseForBreak { get; set; }
        public int ResumeAfterBreakCount { get; private set; }
        public bool IsProtectionDegraded => !IsHealthy;
        public string? ProtectionStatusMessage => "Test blocker active";

        public TestBlockerModule()
        {
            _weakenAction = Action.Create("WeakenRule", "Weaken rule");
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

        public Task OnSessionStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnSessionEndAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> CanStartCommittedSessionAsync(CancellationToken cancellationToken) => Task.FromResult(true);

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
