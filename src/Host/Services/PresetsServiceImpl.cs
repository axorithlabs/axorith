using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Core.Telemetry;
using Axorith.Sdk.Services;
using Axorith.Telemetry;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
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

        var existingPresets = await presetManager.LoadAllPresetsAsync(context.CancellationToken)
            .ConfigureAwait(false) ?? [];

        var nameConflict = existingPresets.FirstOrDefault(p =>
            string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));

        if (nameConflict != null)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, "Preset with this name already exists"));
        }

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
        TrackPresetTelemetry("PresetCreated", preset, "create");
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

        var existingPresets = await presetManager.LoadAllPresetsAsync(context.CancellationToken)
            .ConfigureAwait(false) ?? [];

        var nameConflict = existingPresets.FirstOrDefault(p =>
            string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase) && p.Id != preset.Id);

        if (nameConflict != null)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, "Preset with this name already exists"));
        }

        await presetManager.SavePresetAsync(preset, context.CancellationToken)
            .ConfigureAwait(false);

        sandboxManager.DisposeSandboxesForPreset(preset.Modules.Select(m => m.InstanceId));

        var response = PresetCodec.ToMessage(preset);
        logger.LogInformation("Updated preset: {PresetId} - {PresetName}", preset.Id, preset.Name);
        TrackPresetTelemetry("PresetUpdated", preset, "update");
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
        var presetExisted = _telemetry.IsEnabled &&
                            await presetManager.GetPresetByIdAsync(presetId, context.CancellationToken)
                                .ConfigureAwait(false) is not null;

        await presetManager.DeletePresetAsync(presetId, context.CancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation("Deleted preset: {PresetId}", presetId);
        var wasDeleted = presetExisted &&
                         await presetManager.GetPresetByIdAsync(presetId, context.CancellationToken)
                             .ConfigureAwait(false) is null;
        if (wasDeleted)
        {
            _telemetry.TrackEvent("PresetDeleted", new Dictionary<string, object?>
            {
                ["presetId"] = presetId.ToString()
            });
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

    private void TrackPresetTelemetry(string eventName, SessionPreset preset, string changeType)
    {
        if (!_telemetry.IsEnabled) return;

        try
        {
            var moduleDefLookup = moduleRegistry.GetAllDefinitions().ToDictionary(m => m.Id, m => m.Name);
            _telemetry.TrackEvent(eventName, ProductAnalyticsProperties.Preset(preset, moduleDefLookup, _secureStorage));

            foreach (var module in preset.Modules)
            {
                _telemetry.TrackEvent("ModuleConfigurationSaved",
                    ProductAnalyticsProperties.ModuleConfigurationSaved(preset, module,
                        moduleDefLookup.GetValueOrDefault(module.ModuleId, "custom"), changeType, _secureStorage));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not track saved preset configuration telemetry.");
        }
    }

}
