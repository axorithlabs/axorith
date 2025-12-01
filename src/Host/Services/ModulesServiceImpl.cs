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
public class ModulesServiceImpl(
    IModuleRegistry moduleRegistry,
    ISessionManager sessionManager,
    SettingUpdateBroadcaster settingBroadcaster,
    IDesignTimeSandboxManager sandboxManager,
    ILogger<ModulesServiceImpl> logger)
    : ModulesService.ModulesServiceBase
{
    public override Task<OperationResult> SyncEdit(SyncEditRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ModuleInstanceId, out var instanceId))
        {
            return Task.FromResult(SessionMapper.CreateResult(false, "Invalid module instance ID",
                [$"Could not parse: {request.ModuleInstanceId}"]));
        }

        try
        {
            sandboxManager.ReBroadcast(instanceId);
            return Task.FromResult(SessionMapper.CreateResult(true, "Design-time state synchronized"));
        }
        catch (InvalidOperationException)
        {
            // No sandbox exists; nothing to sync.
            return Task.FromResult(SessionMapper.CreateResult(true, "No sandbox to synchronize"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to synchronize design-time state for {InstanceId}", instanceId);
            return Task.FromResult(SessionMapper.CreateResult(false, "Failed to synchronize design-time state"));
        }
    }

    public override Task<ListModulesResponse> ListModules(ListModulesRequest request, ServerCallContext context)
    {
        try
        {
            logger.LogDebug("ListModules called");

            var modules = moduleRegistry.GetAllDefinitions();

            if (!string.IsNullOrWhiteSpace(request.Category))
            {
                var category = request.Category.Trim();
                modules = modules
                    .Where(m => !string.IsNullOrEmpty(m.Category) &&
                                string.Equals(m.Category, category, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var response = new ListModulesResponse();
            response.Modules.AddRange(modules.Select(ModuleMapper.ToMessage));

            logger.LogInformation("Returned {Count} module definitions (category filter: {Category})",
                modules.Count,
                string.IsNullOrWhiteSpace(request.Category) ? "<none>" : request.Category);
            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing modules");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to list modules", ex));
        }
    }

    public override async Task<GetModuleSettingsResponse> GetModuleSettings(GetModuleSettingsRequest request,
        ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.ModuleId, out var moduleId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    $"Invalid module ID: {request.ModuleId}"));
            }

            logger.LogDebug("GetModuleSettings called for module {ModuleId}", moduleId);

            var definition = moduleRegistry.GetDefinitionById(moduleId);
            if (definition?.ModuleType == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"Module not found: {moduleId}"));
            }

            var (module, scope) = moduleRegistry.CreateInstance(moduleId);
            if (module == null)
            {
                throw new RpcException(new Status(StatusCode.Internal,
                    $"Failed to create module instance: {moduleId}"));
            }

            try
            {
                try
                {
                    using var initCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await module.InitializeAsync(initCts.Token);
                }
                catch (Exception initEx)
                {
                    logger.LogWarning(initEx, "Module initialization failed during GetModuleSettings");
                }

                var response = new GetModuleSettingsResponse();

                var settings = module.GetSettings();
                foreach (var setting in settings)
                {
                    response.Settings.Add(SettingMapper.ToMessage(setting));
                }

                var actions = module.GetActions();
                foreach (var action in actions)
                {
                    response.Actions.Add(ActionMapper.ToMessage(action));
                }

                logger.LogInformation(
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
            logger.LogError(ex, "Error getting module settings");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to get module settings", ex));
        }
    }

    public override async Task<OperationResult> InvokeAction(InvokeActionRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.ModuleInstanceId, out var instanceId))
            {
                return SessionMapper.CreateResult(false, "Invalid module instance ID",
                    [$"Could not parse: {request.ModuleInstanceId}"]);
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(request.ActionKey);

            logger.LogInformation("InvokeAction (runtime) called: InstanceId={InstanceId}, ActionKey={ActionKey}",
                instanceId, request.ActionKey);

            var module = sessionManager.GetActiveModuleInstanceByInstanceId(instanceId);
            if (module == null)
            {
                return SessionMapper.CreateResult(false, "Module instance is not active",
                    [$"Module instance {instanceId} is not running"]);
            }

            var action = module.GetActions().FirstOrDefault(a => a.Key == request.ActionKey);
            if (action == null)
            {
                return SessionMapper.CreateResult(false, "Action not found",
                    [$"Action '{request.ActionKey}' not found in module"]);
            }

            logger.LogDebug("Invoking runtime action {ActionKey} on instance {InstanceId}", request.ActionKey,
                instanceId);
            await action.InvokeAsync();

            logger.LogInformation("Runtime action {ActionKey} on {InstanceId} completed successfully",
                request.ActionKey, instanceId);
            return SessionMapper.CreateResult(true, "Action completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error invoking action");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to invoke action", ex));
        }
    }

    public override async Task<OperationResult> InvokeDesignTimeAction(InvokeDesignTimeActionRequest request,
        ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.ModuleId, out var parsedId))
            {
                return SessionMapper.CreateResult(false, "Invalid module ID",
                    [$"Could not parse: {request.ModuleId}"]);
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(request.ActionKey);

            logger.LogInformation("InvokeDesignTimeAction called: Id={Id}, ActionKey={ActionKey}",
                parsedId, request.ActionKey);

            try
            {
                var invoked = await sandboxManager.TryInvokeActionAsync(parsedId, request.ActionKey,
                        context.CancellationToken)
                    .ConfigureAwait(false);

                if (invoked)
                {
                    logger.LogInformation(
                        "Design-time sandbox action {ActionKey} completed successfully for instance {InstanceId}",
                        request.ActionKey, parsedId);
                    return SessionMapper.CreateResult(true, "Action completed successfully");
                }
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex,
                    "Action {ActionKey} not found in design-time sandbox {InstanceId}",
                    request.ActionKey, parsedId);
                return SessionMapper.CreateResult(false, "Action not found",
                    [$"Action '{request.ActionKey}' not found in module"]);
            }

            var (module, scope) = moduleRegistry.CreateInstance(parsedId);
            if (module == null)
            {
                return SessionMapper.CreateResult(false, "Module not found",
                    [$"Module with ID {parsedId} could not be instantiated"]);
            }

            try
            {
                var action = module.GetActions().FirstOrDefault(a => a.Key == request.ActionKey);
                if (action == null)
                {
                    return SessionMapper.CreateResult(false, "Action not found",
                        [$"Action '{request.ActionKey}' not found in module"]);
                }

                logger.LogDebug("Invoking design-time action {ActionKey} asynchronously on temporary instance",
                    request.ActionKey);
                await action.InvokeAsync().ConfigureAwait(false);

                logger.LogInformation("Design-time action {ActionKey} completed successfully", request.ActionKey);
                return SessionMapper.CreateResult(true, "Action completed successfully");
            }
            finally
            {
                module.Dispose();
                scope?.Dispose();
                logger.LogDebug("Temporary design-time module instance disposed after action completion");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error invoking design-time action");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to invoke design-time action", ex));
        }
    }

    public override Task<OperationResult> UpdateSetting(UpdateSettingRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.ModuleInstanceId, out var instanceId))
            {
                return Task.FromResult(SessionMapper.CreateResult(false, "Invalid module instance ID",
                    [$"Could not parse: {request.ModuleInstanceId}"]));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(request.SettingKey);

            logger.LogDebug("UpdateSetting called: {InstanceId}.{SettingKey}",
                instanceId, request.SettingKey);

            var stringValue = request.ValueCase switch
            {
                UpdateSettingRequest.ValueOneofCase.StringValue => request.StringValue,
                UpdateSettingRequest.ValueOneofCase.BoolValue => request.BoolValue.ToString(),
                UpdateSettingRequest.ValueOneofCase.NumberValue => request.NumberValue.ToString(),
                UpdateSettingRequest.ValueOneofCase.IntValue => request.IntValue.ToString(),
                _ => null
            };

            var activeModule = sessionManager.GetActiveModuleInstanceByInstanceId(instanceId);
            if (activeModule != null)
            {
                var setting = activeModule.GetSettings().FirstOrDefault(s => s.Key == request.SettingKey);
                if (setting != null)
                {
                    setting.SetValueFromString(stringValue);
                    logger.LogInformation("Setting {SettingKey} updated on running module {InstanceId}",
                        request.SettingKey, instanceId);

                    object? broadcastValue = request.ValueCase switch
                    {
                        UpdateSettingRequest.ValueOneofCase.StringValue => request.StringValue,
                        UpdateSettingRequest.ValueOneofCase.BoolValue => request.BoolValue,
                        UpdateSettingRequest.ValueOneofCase.NumberValue => request.NumberValue,
                        UpdateSettingRequest.ValueOneofCase.IntValue => request.IntValue,
                        _ => null
                    };

                    _ = settingBroadcaster.BroadcastUpdateAsync(instanceId, request.SettingKey,
                        SettingProperty.Value, broadcastValue);

                    return Task.FromResult(SessionMapper.CreateResult(true, "Setting updated successfully"));
                }

                logger.LogWarning("Setting {SettingKey} not found in module {InstanceId}",
                    request.SettingKey, instanceId);
                return Task.FromResult(SessionMapper.CreateResult(false, "Setting not found in module",
                    [$"Setting '{request.SettingKey}' does not exist in module"]));
            }

            try
            {
                sandboxManager.ApplySetting(instanceId, request.SettingKey, stringValue);
                return Task.FromResult(SessionMapper.CreateResult(true, "Setting applied in design-time sandbox"));
            }
            catch (InvalidOperationException)
            {
                logger.LogWarning(
                    "Failed to update setting {SettingKey} for {InstanceId}: no active module and no design-time sandbox",
                    request.SettingKey, instanceId);

                return Task.FromResult(SessionMapper.CreateResult(false,
                    "No active module or design-time sandbox for this setting",
                    [$"Module instance {instanceId} is not running and no design-time sandbox exists."]));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating setting");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to update setting", ex));
        }
    }

    public override async Task<OperationResult> BeginEdit(BeginEditRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ModuleId, out var moduleId))
        {
            return SessionMapper.CreateResult(false, "Invalid module ID", [$"Could not parse: {request.ModuleId}"]);
        }

        if (!Guid.TryParse(request.ModuleInstanceId, out var instanceId))
        {
            return SessionMapper.CreateResult(false, "Invalid module instance ID",
                [$"Could not parse: {request.ModuleInstanceId}"]);
        }

        var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var sv in request.InitialValues)
        {
            var str = sv.ValueCase switch
            {
                SettingValue.ValueOneofCase.StringValue => sv.StringValue,
                SettingValue.ValueOneofCase.BoolValue => sv.BoolValue.ToString(),
                SettingValue.ValueOneofCase.NumberValue => sv.NumberValue.ToString(),
                SettingValue.ValueOneofCase.IntValue => sv.IntValue.ToString(),
                _ => null
            };
            snapshot[sv.Key] = str;
        }

        await sandboxManager.EnsureAsync(instanceId, moduleId, snapshot, context.CancellationToken)
            .ConfigureAwait(false);
        return SessionMapper.CreateResult(true, "Design-time edit started");
    }

    public override Task<OperationResult> EndEdit(EndEditRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ModuleInstanceId, out var instanceId))
        {
            return Task.FromResult(SessionMapper.CreateResult(false, "Invalid module instance ID",
                [$"Could not parse: {request.ModuleInstanceId}"]));
        }

        sandboxManager.DisposeSandbox(instanceId);
        return Task.FromResult(SessionMapper.CreateResult(true, "Design-time edit ended"));
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

            logger.LogInformation("Client {SubscriberId} started streaming setting updates (filter: {Filter})",
                subscriberId, filter);

            await settingBroadcaster.SubscribeAsync(subscriberId, request.ModuleInstanceId,
                    responseStream, context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Client {SubscriberId} setting update stream cancelled", subscriberId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error streaming setting updates for {SubscriberId}", subscriberId);
            throw;
        }
    }

    public override async Task<ValidationResponse> ValidateSettings(ValidateSettingsRequest request,
        ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.ModuleId, out var moduleId))
            {
                return new ValidationResponse { IsValid = false, Message = "Invalid Module ID" };
            }

            if (!Guid.TryParse(request.ModuleInstanceId, out var instanceId))
            {
                return new ValidationResponse { IsValid = false, Message = "Invalid Instance ID" };
            }

            var settingsDict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var sv in request.Values)
            {
                var str = sv.ValueCase switch
                {
                    SettingValue.ValueOneofCase.StringValue => sv.StringValue,
                    SettingValue.ValueOneofCase.BoolValue => sv.BoolValue.ToString(),
                    SettingValue.ValueOneofCase.NumberValue => sv.NumberValue.ToString(),
                    SettingValue.ValueOneofCase.IntValue => sv.IntValue.ToString(),
                    _ => null
                };
                settingsDict[sv.Key] = str;
            }

            await sandboxManager.EnsureAsync(instanceId, moduleId, settingsDict, context.CancellationToken)
                .ConfigureAwait(false);

            var module = sandboxManager.GetModule(instanceId);
            if (module == null)
            {
                return new ValidationResponse { IsValid = false, Message = "Failed to load module instance" };
            }

            foreach (var kv in settingsDict)
            {
                sandboxManager.ApplySetting(instanceId, kv.Key, kv.Value);
            }

            var result = await module.ValidateSettingsAsync(context.CancellationToken).ConfigureAwait(false);

            var response = new ValidationResponse
            {
                IsValid = result.Status != Sdk.ValidationStatus.Error,
                Message = result.Message
            };

            foreach (var fieldError in result.FieldErrors)
            {
                response.FieldErrors.Add(new ValidationError
                {
                    SettingKey = fieldError.Key,
                    ErrorMessage = fieldError.Value,
                    Severity = ValidationSeverity.ValidationError
                });
            }

            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error validating settings");
            return new ValidationResponse { IsValid = false, Message = $"Validation failed: {ex.Message}" };
        }
    }
}