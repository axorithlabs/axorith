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
using CorePreset = Axorith.Core.Models.SessionPreset;
using CoreConfiguredModule = Axorith.Core.Models.ConfiguredModule;
using SdkModuleDefinition = Axorith.Sdk.ModuleDefinition;

namespace Axorith.Host.Tests.Services;

public sealed class PresetServiceTelemetryTests
{
    [Fact]
    public async Task PresetTelemetry_SkipsIdenticalUpdatesAndDescribesChangedAndDeletedConfiguration()
    {
        var directory = Directory.CreateTempSubdirectory("axorith-preset-telemetry-");
        try
        {
            var telemetry = new RecordingTelemetryService();
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
            Assert.Empty(telemetry.Events);

            var focusChanged = PresetCodec.ToMessage(preset);
            focusChanged.FocusCommitment.Mode = (Axorith.Contracts.FocusCommitmentMode)2;
            await service.UpdatePreset(new UpdatePresetRequest { Preset = focusChanged }, GrpcTestContext.Create());
            var focusEvents = telemetry.Events.ToArray();
            Assert.Equal(["PresetUpdated", "ModuleConfigurationSaved"],
                focusEvents.Select(item => item.Name).ToArray());
            Assert.False((bool)focusEvents[1].Properties["moduleConfigurationChanged"]!);

            var moduleChanged = PresetCodec.ToMessage(preset);
            moduleChanged.FocusCommitment.Mode = (Axorith.Contracts.FocusCommitmentMode)2;
            moduleChanged.Modules[0].Settings["PrivateSetting"] = "changed-private-value";
            await service.UpdatePreset(new UpdatePresetRequest { Preset = moduleChanged }, GrpcTestContext.Create());
            var moduleEvent = telemetry.Events.Last(item => item.Name == "ModuleConfigurationSaved");
            Assert.True((bool)moduleEvent.Properties["moduleConfigurationChanged"]!);

            await service.DeletePreset(new DeletePresetRequest { PresetId = preset.Id.ToString("D") },
                GrpcTestContext.Create());
            var deleted = telemetry.Events.Single(item => item.Name == "PresetDeleted").Properties;
            Assert.Equal("strict", deleted["commitmentMode"]);
            Assert.Equal(1, deleted["moduleCount"]);
            Assert.Equal(new[] { "custom" }, (string[])deleted["moduleTypes"]!);
            var state = (Dictionary<string, object?>)deleted["$set"]!;
            Assert.Equal(0, state["presetCount"]);
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

    private sealed class RecordingTelemetryService : ITelemetryService
    {
        public List<(string Name, Dictionary<string, object?> Properties)> Events { get; } = [];
        public bool IsEnabled => true;
        public void SetEnabled(bool enabled) { }
        public void TrackEvent(string eventName, IReadOnlyDictionary<string, object?>? properties = null) =>
            Events.Add((eventName, properties is null ? [] : new Dictionary<string, object?>(properties)));
        public void TrackError(Exception exception, string subsystem, string operation, string severity, bool handled,
            bool fatal, IReadOnlyDictionary<string, object?>? properties = null) { }
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
