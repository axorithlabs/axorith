using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Mappers;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Axorith.Host.Services;

/// <summary>
///     gRPC service implementation for preset management.
///     Wraps Core IPresetManager and translates between protobuf and Core models.
/// </summary>
public class PresetsServiceImpl(IPresetManager presetManager, ILogger<PresetsServiceImpl> logger)
    : PresetsService.PresetsServiceBase
{
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
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    $"Invalid preset ID: {request.PresetId}"));

            logger.LogDebug("GetPreset called for {PresetId}", presetId);

            var presets = await presetManager.LoadAllPresetsAsync(context.CancellationToken)
                .ConfigureAwait(false);

            var preset = presets.FirstOrDefault(p => p.Id == presetId);

            if (preset == null)
                throw new RpcException(new Status(StatusCode.NotFound,
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
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Preset is required"));

            logger.LogDebug("CreatePreset called: {PresetName}", request.Preset.Name);

            var preset = PresetMapper.ToModel(request.Preset);

            if (preset.Id == Guid.Empty) preset.Id = Guid.NewGuid();

            foreach (var module in preset.Modules.Where(module => module.InstanceId == Guid.Empty))
                module.InstanceId = Guid.NewGuid();

            await presetManager.SavePresetAsync(preset, context.CancellationToken)
                .ConfigureAwait(false);

            var response = PresetMapper.ToMessage(preset);
            logger.LogInformation("Created preset: {PresetId} - {PresetName}", preset.Id, preset.Name);
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
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Preset is required"));

            if (!Guid.TryParse(request.Preset.Id, out var presetId) || presetId == Guid.Empty)
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    $"Invalid preset ID: {request.Preset.Id}"));

            logger.LogDebug("UpdatePreset called for {PresetId}", presetId);

            var preset = PresetMapper.ToModel(request.Preset);

            await presetManager.SavePresetAsync(preset, context.CancellationToken)
                .ConfigureAwait(false);

            var response = PresetMapper.ToMessage(preset);
            logger.LogInformation("Updated preset: {PresetId} - {PresetName}", preset.Id, preset.Name);
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
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    $"Invalid preset ID: {request.PresetId}"));

            logger.LogDebug("DeletePreset called for {PresetId}", presetId);

            await presetManager.DeletePresetAsync(presetId, context.CancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation("Deleted preset: {PresetId}", presetId);
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
}