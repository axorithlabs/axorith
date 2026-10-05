using System.Collections.Concurrent;
using System.Net;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using System.Text.Json;
using Axorith.Client.Adapters;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Client.Services;
using Axorith.Core.Models;
using Axorith.Sdk;
using Axorith.Shared.Platform;
using Axorith.Telemetry;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class ModuleActionTelemetryTests
{
    [Fact]
    public async Task ExistingWorkspaceAction_ReportsItsPresetId()
    {
        var handler = new CapturingHttpHandler();
        await using var telemetry = new TelemetryService(new TelemetrySettings
        {
            Enabled = true,
            PostHogApiKey = "test-project-key",
            PostHogHost = "http://localhost/",
            DistinctId = Guid.NewGuid().ToString("D"),
            ApplicationName = "Axorith.Client",
            BatchSize = 100,
            FlushInterval = TimeSpan.FromHours(1)
        }, new CapturingHttpClientFactory(handler));
        var moduleId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        using var action = new ModuleActionAdapter(new ModuleAction("RefreshGames", "Refresh games", null, true),
            new SuccessfulModulesApi(), moduleId, instanceId, "Application Launcher", telemetry, presetId);

        await action.InvokeAsync();
        await telemetry.FlushAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        var invoked = ReadEvents(handler).Single(item => item.GetProperty("event").GetString() == "ModuleActionInvoked");
        var properties = invoked.GetProperty("properties");
        Assert.Equal(moduleId.ToString("D"), properties.GetProperty("moduleId").GetString());
        Assert.Equal(instanceId.ToString("D"), properties.GetProperty("instanceId").GetString());
        Assert.Equal(presetId.ToString("D"), properties.GetProperty("presetId").GetString());
        Assert.Equal("refresh_games", properties.GetProperty("actionKey").GetString());
        Assert.Equal("success", properties.GetProperty("result").GetString());
    }

    private static JsonElement[] ReadEvents(CapturingHttpHandler handler) => handler.Payloads
        .SelectMany(body =>
        {
            using var payload = JsonDocument.Parse(body);
            return payload.RootElement.GetProperty("batch").EnumerateArray().Select(item => item.Clone()).ToArray();
        })
        .ToArray();

    [Fact]
    public async Task OnboardingResult_ListsOnlyCreatedTemplateTypes()
    {
        var modules = new SuccessfulModulesApi(
        [
            new ModuleDefinition { Id = Guid.NewGuid(), Name = "Site Blocker" },
            new ModuleDefinition { Id = Guid.NewGuid(), Name = "App Blocker" },
            new ModuleDefinition { Id = Guid.NewGuid(), Name = "OBS Studio" }
        ]);
        var presets = new RecordingPresetsApi();
        var onboarding = new ClientOnboardingService(new ObsDiscovery(), presets, modules,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ClientOnboardingService>.Instance);

        var result = await onboarding.RunSetupAsync();

        Assert.Equal(3, result.CreatedCount);
        Assert.Equal(new[] { "Developer", "Gamer", "Streamer" }, result.CreatedModuleTypes);
        Assert.Equal(3, presets.Created.Count);
    }

    private sealed class SuccessfulModulesApi(IReadOnlyList<ModuleDefinition>? definitions = null) : IModulesApi
    {
        public Task<IReadOnlyList<ModuleDefinition>> ListModulesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ModuleDefinition>>(definitions ?? Array.Empty<ModuleDefinition>());
        public Task<ModuleSettingsInfo> GetModuleSettingsAsync(Guid moduleId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<OperationResult> InvokeActionAsync(Guid moduleInstanceId, string actionKey,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationResult> InvokeDesignTimeActionAsync(Guid moduleId, Guid moduleInstanceId,
            string actionKey, CancellationToken ct = default) => Task.FromResult(new OperationResult(true, "ok"));
        public Task<OperationResult> UpdateSettingAsync(Guid moduleInstanceId, string settingKey, object? value,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BeginEditResult> BeginEditAsync(Guid moduleId, Guid moduleInstanceId,
            IReadOnlyDictionary<string, object?> initialValues, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<OperationResult> EndEditAsync(Guid moduleInstanceId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<OperationResult> SyncEditAsync(Guid moduleInstanceId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<ValidationResult> ValidateSettingsAsync(Guid moduleId, Guid moduleInstanceId,
            IReadOnlyDictionary<string, object?> values, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public IObservable<SettingUpdate> SettingUpdates => Observable.Empty<SettingUpdate>();
        public Task<IDisposable> SubscribeToSettingUpdatesAsync(Guid moduleInstanceId) =>
            Task.FromResult<IDisposable>(Disposable.Empty);
        public ModuleSettingsInfo? GetCachedSettings(Guid moduleId) => null;
    }

    private sealed class RecordingPresetsApi : IPresetsApi
    {
        public List<SessionPreset> Created { get; } = [];
        public Task<IReadOnlyList<PresetSummary>> ListPresetsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PresetSummary>>([]);
        public Task<SessionPreset?> GetPresetAsync(Guid presetId, CancellationToken ct = default) =>
            Task.FromResult<SessionPreset?>(null);
        public Task<SessionPreset> CreatePresetAsync(SessionPreset preset, CancellationToken ct = default)
        {
            Created.Add(preset);
            return Task.FromResult(preset);
        }
        public Task<SessionPreset> UpdatePresetAsync(SessionPreset preset, CancellationToken ct = default) =>
            Task.FromResult(preset);
        public Task DeletePresetAsync(Guid presetId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ObsDiscovery : IAppDiscoveryService
    {
        public string? FindKnownApp(params string[] processNames) => processNames.Contains("obs64")
            ? @"C:\Apps\obs64.exe"
            : null;
        public List<AppInfo> FindAppsByPublisher(string publisherName) => [];
        public List<AppInfo> GetInstalledApplicationsIndex() => [];
    }

    private sealed class CapturingHttpClientFactory(CapturingHttpHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
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
