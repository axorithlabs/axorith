using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Mappers;
using Axorith.Host.Streaming;
using Grpc.Core;

namespace Axorith.Host.Services;

/// <summary>
///     gRPC service implementation for module management.
///     Wraps Core IModuleRegistry and provides setting update streaming.
/// </summary>
public class ModulesServiceImpl : ModulesService.ModulesServiceBase
{
    private readonly IModuleRegistry _moduleRegistry;
    private readonly ISessionManager _sessionManager;
    private readonly SettingUpdateBroadcaster _settingBroadcaster;
    private readonly ILogger<ModulesServiceImpl> _logger;

    public ModulesServiceImpl(IModuleRegistry moduleRegistry, ISessionManager sessionManager,
        SettingUpdateBroadcaster settingBroadcaster, ILogger<ModulesServiceImpl> logger)
    {
        _moduleRegistry = moduleRegistry ?? throw new ArgumentNullException(nameof(moduleRegistry));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _settingBroadcaster = settingBroadcaster ?? throw new ArgumentNullException(nameof(settingBroadcaster));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override Task<ListModulesResponse> ListModules(ListModulesRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogDebug("ListModules called");

            var modules = _moduleRegistry.GetAllDefinitions();

            var response = new ListModulesResponse();
            response.Modules.AddRange(modules.Select(ModuleMapper.ToMessage));

            _logger.LogInformation("Returned {Count} module definitions", modules.Count);
            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing modules");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to list modules", ex));
        }
    }

    public override async Task<GetModuleSettingsResponse> GetModuleSettings(GetModuleSettingsRequest request,
        ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.ModuleId, out var moduleId))
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    $"Invalid module ID: {request.ModuleId}"));

            _logger.LogDebug("GetModuleSettings called for module {ModuleId}", moduleId);

            var definition = _moduleRegistry.GetDefinitionById(moduleId);
            if (definition?.ModuleType == null)
                throw new RpcException(new Status(StatusCode.NotFound, $"Module not found: {moduleId}"));

            // Create a temporary module instance to get its settings and actions
            var (module, scope) = _moduleRegistry.CreateInstance(moduleId);
            if (module == null)
                throw new RpcException(new Status(StatusCode.Internal,
                    $"Failed to create module instance: {moduleId}"));

            try
            {
                // Initialize module to load dynamic choices (devices, playlists, etc.)
                try
                {
                    using var initCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await module.InitializeAsync(initCts.Token);
                }
                catch (Exception initEx)
                {
                    // Non-fatal - module can still function without initialization
                    _logger.LogWarning(initEx, "Module initialization failed during GetModuleSettings");
                }

                var response = new GetModuleSettingsResponse();

                // Map settings (now with updated choices from InitializeAsync)
                var settings = module.GetSettings();
                foreach (var setting in settings) response.Settings.Add(SettingMapper.ToMessage(setting));

                // Map actions
                var actions = module.GetActions();
                foreach (var action in actions) response.Actions.Add(ActionMapper.ToMessage(action));

                _logger.LogInformation(
                    "Returned {SettingCount} settings and {ActionCount} actions for module {ModuleId}",
                    response.Settings.Count, response.Actions.Count, moduleId);

                return response;
            }
            finally
            {
                module.Dispose();
                scope?.Dispose();
            }
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting module settings");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to get module settings", ex));
        }
    }

    public override async Task<OperationResult> InvokeAction(InvokeActionRequest request, ServerCallContext context)
    {
        try
        {
            // Parse as module definition ID for design-time actions (e.g., OAuth login during preset editing)
            if (!Guid.TryParse(request.ModuleInstanceId, out var moduleId))
                return SessionMapper.CreateResult(false, "Invalid module ID",
                    [$"Could not parse: {request.ModuleInstanceId}"]);

            ArgumentException.ThrowIfNullOrWhiteSpace(request.ActionKey);

            _logger.LogInformation("InvokeAction called: ModuleId={ModuleId}, ActionKey={ActionKey}",
                moduleId, request.ActionKey);

            // Create temporary module instance for design-time actions
            // This allows actions like OAuth login to work while editing presets
            var (module, scope) = _moduleRegistry.CreateInstance(moduleId);
            if (module == null)
                return SessionMapper.CreateResult(false, "Module not found",
                    [$"Module with ID {moduleId} could not be instantiated"]);

            try
            {
                // Find the action
                var action = module.GetActions().FirstOrDefault(a => a.Key == request.ActionKey);
                if (action == null)
                    return SessionMapper.CreateResult(false, "Action not found",
                        [$"Action '{request.ActionKey}' not found in module"]);

                // Invoke the action asynchronously and wait for completion
                // This ensures OAuth login and other long-running actions complete before disposal
                _logger.LogDebug("Invoking action {ActionKey} asynchronously", request.ActionKey);
                await action.InvokeAsync();

                _logger.LogInformation("Action {ActionKey} completed successfully", request.ActionKey);
                return SessionMapper.CreateResult(true, "Action completed successfully");
            }
            finally
            {
                // Cleanup: Dispose temporary instance after action completes
                module.Dispose();
                scope?.Dispose();
                _logger.LogDebug("Module instance disposed after action completion");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invoking action");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to invoke action", ex));
        }
    }

    public override Task<OperationResult> UpdateSetting(UpdateSettingRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.ModuleInstanceId, out var instanceId))
                return Task.FromResult(SessionMapper.CreateResult(false, "Invalid module instance ID",
                    [$"Could not parse: {request.ModuleInstanceId}"]));

            ArgumentException.ThrowIfNullOrWhiteSpace(request.SettingKey);

            _logger.LogDebug("UpdateSetting called: {InstanceId}.{SettingKey}",
                instanceId, request.SettingKey);

            // Extract value from oneof
            string? stringValue = request.ValueCase switch
            {
                UpdateSettingRequest.ValueOneofCase.StringValue => request.StringValue,
                UpdateSettingRequest.ValueOneofCase.BoolValue => request.BoolValue.ToString(),
                UpdateSettingRequest.ValueOneofCase.NumberValue => request.NumberValue.ToString(),
                UpdateSettingRequest.ValueOneofCase.IntValue => request.IntValue.ToString(),
                _ => null
            };

            // Try to apply setting to running module instance
            var activeModule = _sessionManager.GetActiveModuleInstanceByInstanceId(instanceId);
            if (activeModule != null)
            {
                var setting = activeModule.GetSettings().FirstOrDefault(s => s.Key == request.SettingKey);
                if (setting != null)
                {
                    setting.SetValueFromString(stringValue);
                    _logger.LogInformation("Setting {SettingKey} updated on running module {InstanceId}",
                        request.SettingKey, instanceId);

                    // Broadcast the update to connected clients
                    object? broadcastValue = request.ValueCase switch
                    {
                        UpdateSettingRequest.ValueOneofCase.StringValue => request.StringValue,
                        UpdateSettingRequest.ValueOneofCase.BoolValue => request.BoolValue,
                        UpdateSettingRequest.ValueOneofCase.NumberValue => request.NumberValue,
                        UpdateSettingRequest.ValueOneofCase.IntValue => request.IntValue,
                        _ => null
                    };

                    _ = _settingBroadcaster.BroadcastUpdateAsync(instanceId, request.SettingKey,
                        SettingProperty.Value, broadcastValue);

                    return Task.FromResult(SessionMapper.CreateResult(true, "Setting updated successfully"));
                }

                _logger.LogWarning("Setting {SettingKey} not found in module {InstanceId}",
                    request.SettingKey, instanceId);
                return Task.FromResult(SessionMapper.CreateResult(false, "Setting not found in module",
                    [$"Setting '{request.SettingKey}' does not exist in module"]));
            }

            // Module not running - only broadcast (for design-time UI updates)
            object? fallbackValue = request.ValueCase switch
            {
                UpdateSettingRequest.ValueOneofCase.StringValue => request.StringValue,
                UpdateSettingRequest.ValueOneofCase.BoolValue => request.BoolValue,
                UpdateSettingRequest.ValueOneofCase.NumberValue => request.NumberValue,
                UpdateSettingRequest.ValueOneofCase.IntValue => request.IntValue,
                _ => null
            };

            _ = _settingBroadcaster.BroadcastUpdateAsync(instanceId, request.SettingKey,
                SettingProperty.Value, fallbackValue);

            _logger.LogDebug("Setting update broadcasted (module not running)");
            return Task.FromResult(SessionMapper.CreateResult(true, "Setting update broadcasted (module not running)"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating setting");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to update setting", ex));
        }
    }

    public override async Task StreamSettingUpdates(StreamSettingUpdatesRequest request,
        IServerStreamWriter<SettingUpdate> responseStream, ServerCallContext context)
    {
        var subscriberId = Guid.NewGuid().ToString();

        try
        {
            var filter = string.IsNullOrWhiteSpace(request.ModuleInstanceId)
                ? "all"
                : request.ModuleInstanceId;

            _logger.LogInformation("Client {SubscriberId} started streaming setting updates (filter: {Filter})",
                subscriberId, filter);

            await _settingBroadcaster.SubscribeAsync(subscriberId, request.ModuleInstanceId,
                    responseStream, context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Client {SubscriberId} setting update stream cancelled", subscriberId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming setting updates for {SubscriberId}", subscriberId);
            throw;
        }
    }
}