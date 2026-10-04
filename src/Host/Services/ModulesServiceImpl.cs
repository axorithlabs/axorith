using System.Globalization;
using System.Collections.Concurrent;
using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Mappers;
using Axorith.Host.Streaming;
using Axorith.Sdk;
using Grpc.Core;

namespace Axorith.Host.Services;

public class ModulesServiceImpl(
    IModuleRegistry moduleRegistry,
    ISessionManager sessionManager,
    SettingUpdateBroadcaster settingBroadcaster,
    DesignTimeSandboxManager sandboxManager,
    ILogger<ModulesServiceImpl> logger)
    : ModulesService.ModulesServiceBase
{
    private sealed record CachedSchema(DateTime? Timestamp, GetModuleSettingsResponse Response);

    private readonly ConcurrentDictionary<Guid, CachedSchema> _schemaCache = new();

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
            logger.LogDebug("Re-broadcasted current state for sandbox {InstanceId}", instanceId);
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

    public override async Task<GetModuleSettingsResponse> GetModuleSettings(GetModuleSettingsRequest request,
        ServerCallContext context)
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

        if (_schemaCache.TryGetValue(moduleId, out var cached) &&
            cached.Timestamp == definition.AssemblyTimestampUtc)
        {
            logger.LogInformation(
                "Returned cached settings schema for module {ModuleId} (timestamp {Timestamp})",
                moduleId, definition.AssemblyTimestampUtc?.ToString("O") ?? "<null>");
            return cached.Response.Clone();
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

            var response = BuildSettingsResponseFromModule(module);

            _schemaCache[moduleId] = new CachedSchema(definition.AssemblyTimestampUtc, response);

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

    public override async Task<OperationResult> InvokeAction(InvokeActionRequest request, ServerCallContext context)
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

        if (module is ICommittedSessionValidator && IsCommittedSessionActive())
        {
            return SessionMapper.CreateResult(false,
                "Blocker actions are unavailable during a committed session.");
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

    public override async Task<OperationResult> InvokeDesignTimeAction(InvokeDesignTimeActionRequest request,
        ServerCallContext context)
    {

        if (!Guid.TryParse(request.ModuleId, out var moduleId))
        {
            return SessionMapper.CreateResult(false, "Invalid module ID",
                [$"Could not parse: {request.ModuleId}"]);
        }

        Guid? moduleInstanceId = null;
        if (!string.IsNullOrWhiteSpace(request.ModuleInstanceId))
        {
            if (!Guid.TryParse(request.ModuleInstanceId, out var parsedInstanceId))
            {
                return SessionMapper.CreateResult(false, "Invalid module instance ID",
                    [$"Could not parse: {request.ModuleInstanceId}"]);
            }

            moduleInstanceId = parsedInstanceId;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActionKey);

        logger.LogInformation(
            "InvokeDesignTimeAction called: ModuleId={ModuleId}, InstanceId={InstanceId}, ActionKey={ActionKey}",
            moduleId, moduleInstanceId?.ToString() ?? "<none>", request.ActionKey);

        if (moduleInstanceId.HasValue)
        {
            try
            {
                var invoked = await sandboxManager.TryInvokeActionAsync(moduleInstanceId.Value, request.ActionKey,
                        context.CancellationToken)
                    .ConfigureAwait(false);

                if (invoked)
                {
                    logger.LogInformation(
                        "Design-time sandbox action {ActionKey} completed successfully for instance {InstanceId}",
                        request.ActionKey, moduleInstanceId.Value);
                    return SessionMapper.CreateResult(true, "Action completed successfully");
                }

                return SessionMapper.CreateResult(false,
                    "Design-time sandbox not found for module instance",
                    [$"Module instance {moduleInstanceId.Value} does not have an active design-time sandbox"]);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex,
                    "Action {ActionKey} not found in design-time sandbox {InstanceId}",
                    request.ActionKey, moduleInstanceId.Value);
                return SessionMapper.CreateResult(false, "Action not found",
                    [$"Action '{request.ActionKey}' not found in module"]);
            }
        }

        var (module, scope) = moduleRegistry.CreateInstance(moduleId);
        if (module == null)
        {
            return SessionMapper.CreateResult(false, "Module not found",
                [$"Module with ID {moduleId} could not be instantiated"]);
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

    private static GetModuleSettingsResponse BuildSettingsResponseFromModule(IModule module)
    {
        var response = new GetModuleSettingsResponse();
        response.Settings.AddRange(module.GetSettings().Select(SettingMapper.ToMessage));
        response.Actions.AddRange(module.GetActions().Select(ActionMapper.ToMessage));
        return response;
    }

    public override Task<OperationResult> UpdateSetting(UpdateSettingRequest request, ServerCallContext context)
    {

        if (!Guid.TryParse(request.ModuleInstanceId, out var instanceId))
        {
            return Task.FromResult(SessionMapper.CreateResult(false, "Invalid module instance ID",
                [$"Could not parse: {request.ModuleInstanceId}"]));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.SettingKey);

        logger.LogDebug("UpdateSetting called: {InstanceId}.{SettingKey}",
            instanceId, request.SettingKey);

        var value = SettingValueCodec.Get(request);
        var stringValue = value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value?.ToString();

        var activeModule = sessionManager.GetActiveModuleInstanceByInstanceId(instanceId);
        if (activeModule != null)
        {
            if (IsCommittedSessionActive())
            {
                return Task.FromResult(SessionMapper.CreateResult(false,
                    "Module settings cannot be changed during a committed session."));
            }

            var setting = activeModule.GetSettings().FirstOrDefault(s => s.Key == request.SettingKey);
            if (setting != null)
            {
                setting.SetValueFromString(stringValue);
                logger.LogInformation("Setting {SettingKey} updated on running module {InstanceId}",
                    request.SettingKey, instanceId);

                _ = settingBroadcaster.BroadcastUpdateAsync(instanceId, request.SettingKey,
                    SettingProperty.Value, value);

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


    private bool IsCommittedSessionActive() =>
        sessionManager.ActiveSession?.FocusCommitment.IsCommitted == true;

    public override async Task<BeginEditResponse> BeginEdit(BeginEditRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ModuleId, out var moduleId))
        {
            return new BeginEditResponse
            {
                Result = SessionMapper.CreateResult(false, "Invalid module ID",
                    [$"Could not parse: {request.ModuleId}"])
            };
        }

        if (!Guid.TryParse(request.ModuleInstanceId, out var instanceId))
        {
            return new BeginEditResponse
            {
                Result = SessionMapper.CreateResult(false, "Invalid module instance ID",
                    [$"Could not parse: {request.ModuleInstanceId}"])
            };
        }

        var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in request.InitialValues)
            snapshot[value.Key] = SettingValueCodec.GetString(value);

        var response = new BeginEditResponse();

        try
        {
            await sandboxManager.EnsureAsync(instanceId, moduleId, snapshot, context.CancellationToken)
                .ConfigureAwait(false);

            var module = sandboxManager.GetModule(instanceId);
            if (module == null)
            {
                response.Result = SessionMapper.CreateResult(false,
                    "Failed to load module instance for design-time sandbox");
                return response;
            }

            var definition = moduleRegistry.GetDefinitionById(moduleId);
            var schema = BuildSettingsResponseFromModule(module);
            if (definition != null)
            {
                _schemaCache[moduleId] = new CachedSchema(definition.AssemblyTimestampUtc, schema);
            }

            response.Settings.AddRange(schema.Settings);
            response.Actions.AddRange(schema.Actions);
            response.Result = SessionMapper.CreateResult(true, "Design-time edit started");
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start design-time edit for {ModuleId}", moduleId);
            response.Result = SessionMapper.CreateResult(false, "Failed to start design-time edit",
                [$"Exception: {ex.Message}"]);
            return response;
        }
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
        var filter = string.IsNullOrWhiteSpace(request.ModuleInstanceId) ? "all" : request.ModuleInstanceId;
        logger.LogInformation("Client {SubscriberId} started streaming setting updates (filter: {Filter})",
            subscriberId, filter);
        await settingBroadcaster.SubscribeAsync(subscriberId, request.ModuleInstanceId, responseStream,
            context.CancellationToken).ConfigureAwait(false);
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
            foreach (var value in request.Values)
                settingsDict[value.Key] = SettingValueCodec.GetString(value);

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
                IsValid = result.Status != ValidationStatus.Error,
                Message = result.Message,
                IsWarning = result.Status == ValidationStatus.Warning
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
