using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Mappers;
using Axorith.Telemetry;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Axorith.Host.Services;

/// <summary>
///     gRPC service implementation for preset management.
///     Wraps Core IPresetManager and translates between protobuf and Core models.
/// </summary>
public class PresetsServiceImpl(
    IPresetManager presetManager,
    IDesignTimeSandboxManager sandboxManager,
    IModuleRegistry moduleRegistry,
    ILogger<PresetsServiceImpl> logger,
    ITelemetryService? telemetry = null)
    : PresetsService.PresetsServiceBase
{
    private readonly ITelemetryService _telemetry = telemetry ?? new NoopTelemetryService();

    /// <summary>
    ///     Retrieves all session presets from persistent storage.
    /// </summary>
    /// <param name="request">Request with optional search filter for preset names.</param>
    /// <param name="context">Server call context with cancellation token.</param>
    /// <returns>List of preset summaries with ID, name, and module count.</returns>
    public override async Task<ListPresetsResponse> ListPresets(ListPresetsRequest request, ServerCallContext context)
    {
        try
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
            response.Presets.AddRange(filteredPresets.Select(PresetMapper.ToSummary));

            logger.LogInformation("Returned {Count} presets (filter: {Filter})",
                filteredPresets.Count,
                string.IsNullOrWhiteSpace(request.Search) ? "<none>" : request.Search);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing presets");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to list presets", ex));
        }
    }

    public override async Task<Preset> GetPreset(GetPresetRequest request, ServerCallContext context)
    {
        try
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
            var message = PresetMapper.ToMessage(preset);
            logger.LogInformation("Returned preset: {PresetName}", preset.Name);
            return message;
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting preset {PresetId}", request.PresetId);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to get preset", ex));
        }
    }

    public override async Task<Preset> CreatePreset(CreatePresetRequest request, ServerCallContext context)
    {
        try
        {
            if (request.Preset == null)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Preset is required"));
            }

            logger.LogDebug("CreatePreset called: {PresetName}", request.Preset.Name);

            var preset = PresetMapper.ToModel(request.Preset);

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

            var response = PresetMapper.ToMessage(preset);
            logger.LogInformation("Created preset: {PresetId} - {PresetName}", preset.Id, preset.Name);
            TrackPresetTelemetry("PresetCreated", preset);
            return response;
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating preset");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to create preset", ex));
        }
    }

    public override async Task<Preset> UpdatePreset(UpdatePresetRequest request, ServerCallContext context)
    {
        try
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

            var preset = PresetMapper.ToModel(request.Preset);

            await presetManager.SavePresetAsync(preset, context.CancellationToken)
                .ConfigureAwait(false);

            sandboxManager.DisposeSandboxesForPreset(preset.Modules.Select(m => m.InstanceId));

            var response = PresetMapper.ToMessage(preset);
            logger.LogInformation("Updated preset: {PresetId} - {PresetName}", preset.Id, preset.Name);
            TrackPresetTelemetry("PresetUpdated", preset);
            return response;
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating preset {PresetId}", request.Preset?.Id);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to update preset", ex));
        }
    }

    public override async Task<Empty> DeletePreset(DeletePresetRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.PresetId, out var presetId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    $"Invalid preset ID: {request.PresetId}"));
            }

            logger.LogDebug("DeletePreset called for {PresetId}", presetId);

            await presetManager.DeletePresetAsync(presetId, context.CancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation("Deleted preset: {PresetId}", presetId);
            _telemetry.TrackEvent("PresetDeleted", new Dictionary<string, object?>
            {
                ["presetId"] = presetId.ToString()
            });
            return new Empty();
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting preset {PresetId}", request.PresetId);
            throw new RpcException(new Status(StatusCode.Internal, "Failed to delete preset", ex));
        }
    }

    private void TrackPresetTelemetry(string eventName, SessionPreset preset)
    {
        if (!_telemetry.IsEnabled)
        {
            return;
        }

        var allModuleDefs = moduleRegistry.GetAllDefinitions();
        var moduleDefLookup = allModuleDefs.ToDictionary(m => m.Id, m => m.Name);

        var moduleIds = preset.Modules.Select(m => m.ModuleId.ToString()).ToArray();
        var settingsKeys = preset.Modules
            .SelectMany(m => m.Settings.Keys)
            .Distinct()
            .Take(64) // cap to avoid oversize
            .ToArray();

        var modulesDetailed = preset.Modules.Select(m =>
        {
            var moduleName = moduleDefLookup.TryGetValue(m.ModuleId, out var name) ? name : "Unknown";
            return new
            {
                moduleId = m.ModuleId.ToString(),
                moduleName = TelemetryGuard.SafeString(moduleName),
                instanceId = m.InstanceId.ToString(),
                customName = !string.IsNullOrWhiteSpace(m.CustomName),
                startDelaySec = (int)m.StartDelay.TotalSeconds,
                settingsKeys = m.Settings.Keys.Take(32).ToArray(),
                settingsCount = m.Settings.Count,
                settings = m.Settings
                    .Take(32)
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => new
                        {
                            len = kvp.Value?.Length ?? 0,
                            val = TelemetryGuard.SafeString(kvp.Value, 128)
                        })
            };
        }).ToArray();

        _telemetry.TrackEvent(eventName, new Dictionary<string, object?>
        {
            ["presetId"] = preset.Id.ToString(),
            ["presetName"] = TelemetryGuard.SafeString(preset.Name, 128),
            ["presetNameLength"] = preset.Name?.Length ?? 0,
            ["moduleCount"] = preset.Modules.Count,
            ["moduleIds"] = moduleIds,
            ["settingsKeyCount"] = settingsKeys.Length,
            ["settingsKeys"] = settingsKeys,
            ["modules"] = modulesDetailed
        });
    }
}