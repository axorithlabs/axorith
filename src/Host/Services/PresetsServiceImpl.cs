using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Core.Telemetry;
using Axorith.Sdk.Services;
using Axorith.Telemetry;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using CoreConfiguredModule = Axorith.Core.Models.ConfiguredModule;
using FocusCommitmentMode = Axorith.Core.Models.FocusCommitmentMode;

namespace Axorith.Host.Services;

public class PresetsServiceImpl(
    IPresetManager presetManager,
    IScheduleManager scheduleManager,
    DesignTimeSandboxManager sandboxManager,
    IModuleRegistry moduleRegistry,
    ISessionManager sessionManager,
    ILogger<PresetsServiceImpl> logger,
    ITelemetryService? telemetry = null,
    ISecureStorageService? secureStorage = null)
    : PresetsService.PresetsServiceBase
{
    private readonly ITelemetryService _telemetry = telemetry ?? NoopTelemetryService.Instance;
    private readonly ISecureStorageService? _secureStorage = secureStorage;

    public override async Task<ListPresetsResponse> ListPresets(ListPresetsRequest request, ServerCallContext context)
    {
        var presets = await presetManager.LoadAllPresetsAsync(context.CancellationToken)
            .ConfigureAwait(false);

        var filteredPresets = presets;
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            filteredPresets = presets
                .Where(p => !string.IsNullOrEmpty(p.Name) &&
                            p.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var response = new ListPresetsResponse();
        response.Presets.AddRange(filteredPresets.Select(PresetCodec.ToSummary));

        logger.LogInformation("Returned {Count} presets (filter: {Filter})",
            filteredPresets.Count,
            string.IsNullOrWhiteSpace(request.Search) ? "<none>" : request.Search);
        return response;
    }

    public override async Task<Preset> GetPreset(GetPresetRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.PresetId, out var presetId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"Invalid preset ID: {request.PresetId}"));
        }

        logger.LogDebug("GetPreset called for {PresetId}", presetId);

        var presets = await presetManager.LoadAllPresetsAsync(context.CancellationToken)
            .ConfigureAwait(false);

        var preset = presets.FirstOrDefault(p => p.Id == presetId) ?? throw new RpcException(new Status(
            StatusCode.NotFound,
            $"Preset not found: {presetId}"));
        var message = PresetCodec.ToMessage(preset);
        logger.LogInformation("Returned preset: {PresetName}", preset.Name);
        return message;
    }

    public override async Task<Preset> CreatePreset(CreatePresetRequest request, ServerCallContext context)
    {
        if (request.Preset == null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Preset is required"));
        }

        logger.LogDebug("CreatePreset called: {PresetName}", request.Preset.Name);

        var preset = PresetCodec.ToModel(request.Preset);
        await EnsurePresetNameAvailableAsync(preset, allowSameId: false, context.CancellationToken)
            .ConfigureAwait(false);

        if (preset.Id == Guid.Empty)
        {
            preset.Id = Guid.NewGuid();
        }

        foreach (var module in preset.Modules.Where(module => module.InstanceId == Guid.Empty))
        {
            module.InstanceId = Guid.NewGuid();
        }

        await presetManager.SavePresetAsync(preset, context.CancellationToken)
            .ConfigureAwait(false);

        sandboxManager.DisposeSandboxesForPreset(preset.Modules.Select(m => m.InstanceId));

        var response = PresetCodec.ToMessage(preset);
        logger.LogInformation("Created preset: {PresetId} - {PresetName}", preset.Id, preset.Name);
        await TrackPresetTelemetryAsync("PresetCreated", preset, "create", null, context.CancellationToken)
            .ConfigureAwait(false);
        return response;
    }

    public override async Task<Preset> UpdatePreset(UpdatePresetRequest request, ServerCallContext context)
    {
        if (request.Preset == null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Preset is required"));
        }

        if (!Guid.TryParse(request.Preset.Id, out var presetId) || presetId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"Invalid preset ID: {request.Preset.Id}"));
        }

        logger.LogDebug("UpdatePreset called for {PresetId}", presetId);

        await EnsurePresetMutableAsync(presetId, context.CancellationToken).ConfigureAwait(false);

        var preset = PresetCodec.ToModel(request.Preset);
        var previousPreset = await presetManager.GetPresetByIdAsync(presetId, context.CancellationToken)
            .ConfigureAwait(false);
        if (previousPreset is not null) previousPreset = new SessionPreset(previousPreset);
        var configurationChanged = previousPreset is null || !PresetsEqual(previousPreset, preset);
        await EnsurePresetNameAvailableAsync(preset, allowSameId: true, context.CancellationToken)
            .ConfigureAwait(false);

        await presetManager.SavePresetAsync(preset, context.CancellationToken)
            .ConfigureAwait(false);

        sandboxManager.DisposeSandboxesForPreset(preset.Modules.Select(m => m.InstanceId));

        var response = PresetCodec.ToMessage(preset);
        logger.LogInformation("Updated preset: {PresetId} - {PresetName}", preset.Id, preset.Name);
        if (configurationChanged)
        {
            await TrackPresetTelemetryAsync("PresetUpdated", preset, "update", previousPreset,
                context.CancellationToken).ConfigureAwait(false);
        }
        return response;
    }

    public override async Task<Empty> DeletePreset(DeletePresetRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.PresetId, out var presetId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"Invalid preset ID: {request.PresetId}"));
        }

        logger.LogDebug("DeletePreset called for {PresetId}", presetId);

        await EnsurePresetMutableAsync(presetId, context.CancellationToken).ConfigureAwait(false);
        var deletedPreset = _telemetry.IsEnabled
            ? await presetManager.GetPresetByIdAsync(presetId, context.CancellationToken).ConfigureAwait(false)
            : null;
        if (deletedPreset is not null) deletedPreset = new SessionPreset(deletedPreset);

        await presetManager.DeletePresetAsync(presetId, context.CancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation("Deleted preset: {PresetId}", presetId);
        var wasDeleted = deletedPreset is not null &&
                         await presetManager.GetPresetByIdAsync(presetId, context.CancellationToken)
                             .ConfigureAwait(false) is null;
        if (wasDeleted)
        {
            await TrackPresetDeletedAsync(deletedPreset!, context.CancellationToken).ConfigureAwait(false);
        }
        return new Empty();
    }

    private async Task EnsurePresetMutableAsync(Guid presetId, CancellationToken cancellationToken)
    {
        var activeSession = sessionManager.ActiveSession;
        if (activeSession?.FocusCommitment.IsCommitted == true &&
            activeSession.FocusCommitment.NextWorkspaceId == presetId)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                "Configuration locked · This Workspace is selected as the next Workspace in the active committed Session."));
        }

        var status = await scheduleManager.GetConfigurationLockStatusAsync(presetId, cancellationToken)
            .ConfigureAwait(false);
        if (!status.IsLocked)
        {
            return;
        }

        var time = status.StartsIn is { } remaining && remaining > TimeSpan.Zero
            ? $"Session starts in {(int)Math.Ceiling(remaining.TotalMinutes)} min"
            : "Session starts now";
        throw new RpcException(new Status(StatusCode.FailedPrecondition, $"Configuration locked · {time}."));
    }

    private async Task EnsurePresetNameAvailableAsync(SessionPreset preset, bool allowSameId,
        CancellationToken cancellationToken)
    {
        var existing = await presetManager.LoadAllPresetsAsync(cancellationToken).ConfigureAwait(false) ?? [];
        if (existing.Any(candidate =>
                string.Equals(candidate.Name, preset.Name, StringComparison.OrdinalIgnoreCase) &&
                (!allowSameId || candidate.Id != preset.Id)))
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, "Preset with this name already exists"));
        }
    }

    private async Task TrackPresetTelemetryAsync(string eventName, SessionPreset preset, string changeType,
        SessionPreset? previousPreset, CancellationToken cancellationToken)
    {
        if (!_telemetry.IsEnabled) return;

        try
        {
            var moduleDefLookup = moduleRegistry.GetAllDefinitions().ToDictionary(m => m.Id, m => m.Name);
            var properties = ProductAnalyticsProperties.Preset(preset, moduleDefLookup, _secureStorage);
            await AddProductStateAsync(properties, cancellationToken).ConfigureAwait(false);
            _telemetry.TrackEvent(eventName, properties);

            foreach (var module in preset.Modules)
            {
                var previousModule = previousPreset?.Modules.FirstOrDefault(candidate =>
                    candidate.InstanceId == module.InstanceId);
                _telemetry.TrackEvent("ModuleConfigurationSaved",
                    ProductAnalyticsProperties.ModuleConfigurationSaved(preset, module,
                        moduleDefLookup.GetValueOrDefault(module.ModuleId, "custom"), changeType, _secureStorage,
                        previousPreset is null || previousModule is null || !ModulesEqual(previousModule, module)));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not track saved preset configuration telemetry.");
        }
    }

    private async Task TrackPresetDeletedAsync(SessionPreset preset, CancellationToken cancellationToken)
    {
        try
        {
            var moduleDefLookup = moduleRegistry.GetAllDefinitions().ToDictionary(m => m.Id, m => m.Name);
            var properties = ProductAnalyticsProperties.Preset(preset, moduleDefLookup, _secureStorage);
            await AddProductStateAsync(properties, cancellationToken).ConfigureAwait(false);
            _telemetry.TrackEvent("PresetDeleted", properties);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not track deleted preset configuration telemetry.");
        }
    }

    private async Task<Dictionary<string, object?>> ProductStateAsync(CancellationToken cancellationToken)
    {
        var presets = await presetManager.LoadAllPresetsAsync(cancellationToken).ConfigureAwait(false);
        var schedules = await scheduleManager.ListSchedulesAsync(cancellationToken).ConfigureAwait(false);
        return ProductAnalyticsProperties.ProductState(presets, schedules);
    }

    private async Task AddProductStateAsync(Dictionary<string, object?> eventProperties,
        CancellationToken cancellationToken)
    {
        try
        {
            eventProperties["$set"] = await ProductStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not refresh telemetry product-state properties.");
        }
    }

    private static bool PresetsEqual(SessionPreset left, SessionPreset right)
    {
        var a = left.FocusCommitment;
        var b = right.FocusCommitment;
        return left.Id == right.Id && string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
               a.Mode == b.Mode && a.EndCondition == b.EndCondition && a.Duration == b.Duration &&
               a.EndAtLocalTime == b.EndAtLocalTime && a.EndAtDaysOfWeek.Order().SequenceEqual(b.EndAtDaysOfWeek.Order()) &&
               a.BreakCount == b.BreakCount && a.BreakDuration == b.BreakDuration && a.AfterEnd == b.AfterEnd &&
               a.NextWorkspaceId == b.NextWorkspaceId && a.ScheduleLockMinutes == b.ScheduleLockMinutes &&
               left.Modules.Count == right.Modules.Count &&
               left.Modules.Zip(right.Modules).All(pair => ModulesEqual(pair.First, pair.Second));
    }

    private static bool ModulesEqual(CoreConfiguredModule left, CoreConfiguredModule right) =>
        left.InstanceId == right.InstanceId && left.ModuleId == right.ModuleId &&
        string.Equals(left.CustomName, right.CustomName, StringComparison.Ordinal) && left.StartDelay == right.StartDelay &&
        left.Settings.Count == right.Settings.Count &&
        left.Settings.All(setting => right.Settings.TryGetValue(setting.Key, out var value) &&
                                     string.Equals(setting.Value, value, StringComparison.Ordinal));

}
