using Autofac;
using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Host;
using Axorith.Host.Services;
using Axorith.Host.Streaming;
using Axorith.Sdk;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using CoreConfiguredModule = Axorith.Core.Models.ConfiguredModule;
using CorePreset = Axorith.Core.Models.SessionPreset;
using SdkModuleDefinition = Axorith.Sdk.ModuleDefinition;

namespace Axorith.Host.Tests.Services;

public sealed class PresetServicePersistenceTests
{
    [Fact]
    public async Task ConfigurationUpdatesAndDeletionArePersisted()
    {
        var directory = Directory.CreateTempSubdirectory("axorith-preset-telemetry-");
        try
        {
            var telemetry = NoopTelemetryService.Instance;
            var moduleRegistry = new EmptyModuleRegistry();
            await using var sessionManager = new SessionManager(moduleRegistry, NullLogger<SessionManager>.Instance,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5),
                NoopTelemetryService.Instance);
            using var broadcaster = new SettingUpdateBroadcaster(sessionManager,
                NullLogger<SettingUpdateBroadcaster>.Instance, Options.Create(new Configuration()));
            using var sandboxManager = new DesignTimeSandboxManager(moduleRegistry, broadcaster,
                NullLogger<DesignTimeSandboxManager>.Instance, Options.Create(new Configuration()), sessionManager);
            var presetManager = new PresetManager(Path.Combine(directory.FullName, "presets"),
                NullLogger<PresetManager>.Instance);
            var scheduleManager = new EmptyScheduleManager();
            var service = new PresetsServiceImpl(presetManager, scheduleManager, sandboxManager, moduleRegistry,
                sessionManager, NullLogger<PresetsServiceImpl>.Instance, telemetry);

            var preset = new CorePreset(Guid.NewGuid())
            {
                Name = "Workspace",
                Modules =
                [
                    new CoreConfiguredModule
                    {
                        ModuleId = Guid.NewGuid(),
                        InstanceId = Guid.NewGuid(),
                        Settings = new Dictionary<string, string> { ["PrivateSetting"] = "private-value" }
                    }
                ]
            };
            await presetManager.SavePresetAsync(preset, CancellationToken.None);

            await service.UpdatePreset(new UpdatePresetRequest { Preset = PresetCodec.ToMessage(preset) },
                GrpcTestContext.Create());
            var fresh = new PresetManager(Path.Combine(directory.FullName, "presets"), NullLogger<PresetManager>.Instance);
            Assert.Equal("private-value", (await fresh.GetPresetByIdAsync(preset.Id, CancellationToken.None))!.Modules[0].Settings["PrivateSetting"]);

            var focusChanged = PresetCodec.ToMessage(preset);
            focusChanged.FocusCommitment.Mode = (Axorith.Contracts.FocusCommitmentMode)2;
            await service.UpdatePreset(new UpdatePresetRequest { Preset = focusChanged }, GrpcTestContext.Create());
            Assert.Equal((Axorith.Core.Models.FocusCommitmentMode)2,
                (await fresh.GetPresetByIdAsync(preset.Id, CancellationToken.None))!.FocusCommitment.Mode);

            var moduleChanged = PresetCodec.ToMessage(preset);
            moduleChanged.FocusCommitment.Mode = (Axorith.Contracts.FocusCommitmentMode)2;
            moduleChanged.Modules[0].Settings["PrivateSetting"] = "changed-private-value";
            await service.UpdatePreset(new UpdatePresetRequest { Preset = moduleChanged }, GrpcTestContext.Create());
            Assert.Equal("changed-private-value",
                (await fresh.GetPresetByIdAsync(preset.Id, CancellationToken.None))!.Modules[0].Settings["PrivateSetting"]);

            await service.DeletePreset(new DeletePresetRequest { PresetId = preset.Id.ToString("D") },
                GrpcTestContext.Create());
            Assert.Null(await fresh.GetPresetByIdAsync(preset.Id, CancellationToken.None));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class EmptyModuleRegistry : IModuleRegistry
    {
        public IReadOnlyList<SdkModuleDefinition> GetAllDefinitions() => [];
        public SdkModuleDefinition? GetDefinitionById(Guid moduleId) => null;
        public (IModule? Instance, ILifetimeScope? Scope) CreateInstance(Guid id) => (null, null);
    }

    private sealed class EmptyScheduleManager : IScheduleManager
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartProcessingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<SessionSchedule>> ListSchedulesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SessionSchedule>>([]);
        public Task<IReadOnlyList<SessionSchedule>> GetSchedulesForPresetAsync(Guid presetId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SessionSchedule>>([]);
        public Task<SessionSchedule> SaveScheduleAsync(SessionSchedule schedule, CancellationToken cancellationToken) =>
            Task.FromResult(schedule);
        public Task DeleteScheduleAsync(Guid scheduleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<SessionSchedule?> SetEnabledAsync(Guid scheduleId, bool enabled,
            CancellationToken cancellationToken) => Task.FromResult<SessionSchedule?>(null);
        public Task<Axorith.Core.Models.ConfigurationLockStatus> GetConfigurationLockStatusAsync(Guid presetId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new Axorith.Core.Models.ConfigurationLockStatus(false, null));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

}
