using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Mappers;
using Axorith.Host.Streaming;
using Axorith.Shared.Exceptions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Action = Axorith.Contracts.Action;

namespace Axorith.Host.Services;

/// <summary>
///     gRPC service implementation for session management.
///     Wraps Core ISessionManager and provides event streaming via SessionEventBroadcaster.
/// </summary>
public class SessionsServiceImpl(
    ISessionManager sessionManager,
    IPresetManager presetManager,
    IModuleRegistry moduleRegistry,
    SessionEventBroadcaster eventBroadcaster,
    ILogger<SessionsServiceImpl> logger)
    : SessionsService.SessionsServiceBase
{
    public override Task<SessionState> GetSessionState(GetSessionStateRequest request, ServerCallContext context)
    {
        try
        {
            logger.LogDebug("GetSessionState called");

            var activePreset = sessionManager.ActiveSession;

            var state = new SessionState
            {
                IsActive = activePreset != null
            };

            if (activePreset != null)
            {
                state.PresetId = activePreset.Id.ToString();
                state.PresetName = activePreset.Name;
                state.StartedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

                foreach (var configuredModule in activePreset.Modules)
                {
                    var instance = sessionManager.GetActiveModuleInstanceByInstanceId(configuredModule.InstanceId)
                                   ?? sessionManager.GetActiveModuleInstance(configuredModule.ModuleId);

                    if (instance == null) continue;

                    var definition = moduleRegistry.GetDefinitionById(configuredModule.ModuleId);

                    var moduleState = new ModuleInstanceState
                    {
                        InstanceId = configuredModule.InstanceId.ToString(),
                        ModuleName = definition?.Name ?? "Unknown",
                        CustomName = configuredModule.CustomName ?? string.Empty,
                        Status = ModuleStatus.Running
                    };

                    // Add settings (convert ISetting to proto Setting)
                    foreach (var setting in instance.GetSettings())
                    {
                        var protoSetting = new Setting
                        {
                            Key = setting.Key,
                            Label = setting.GetCurrentLabel(),
                            Description = setting.Description ?? string.Empty,
                            ControlType = (SettingControlType)(int)setting.ControlType,
                            Persistence = (SettingPersistence)(int)setting.Persistence,
                            IsReadOnly = setting.GetCurrentReadOnly(),
                            IsVisible = setting.GetCurrentVisibility(),
                            ValueType = setting.ValueType.Name,
                            StringValue = setting.GetValueAsString()
                        };

                        moduleState.Settings.Add(protoSetting);
                    }

                    foreach (var action in instance.GetActions())
                    {
                        var protoAction = new Action
                        {
                            Key = action.Key
                        };

                        var currentLabel = string.Empty;
                        action.Label.Subscribe(l => currentLabel = l).Dispose();
                        protoAction.Label = currentLabel;

                        var currentEnabled = false;
                        action.IsEnabled.Subscribe(e => currentEnabled = e).Dispose();
                        protoAction.IsEnabled = currentEnabled;

                        moduleState.Actions.Add(protoAction);
                    }

                    state.ModuleStates.Add(moduleState);
                }
            }

            logger.LogDebug("Session active: {IsActive}", state.IsActive);
            return Task.FromResult(state);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting session state");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to get session state", ex));
        }
    }

    public override async Task<OperationResult> StartSession(StartSessionRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.PresetId, out var presetId))
            {
                var result = SessionMapper.CreateResult(false, "Invalid preset ID",
                    [$"Could not parse preset ID: {request.PresetId}"]);
                return result;
            }

            logger.LogInformation("Starting session for preset {PresetId}", presetId);

            // Load preset by ID directly
            var preset = await presetManager.GetPresetByIdAsync(presetId, context.CancellationToken)
                .ConfigureAwait(false);

            if (preset == null)
                return SessionMapper.CreateResult(false, "Preset not found",
                    [$"No preset found with ID: {presetId}"]);

            try
            {
                await sessionManager.StartSessionAsync(preset, context.CancellationToken)
                    .ConfigureAwait(false);

                logger.LogInformation("Session started successfully: {PresetId}", presetId);
                return SessionMapper.CreateResult(true, "Session started successfully");
            }
            catch (SessionException ex)
            {
                logger.LogWarning(ex, "Session start failed: {Message}", ex.Message);
                return SessionMapper.CreateResult(false, ex.Message, [ex.Message]);
            }
            catch (InvalidSettingsException ex)
            {
                logger.LogWarning(ex, "Session start failed due to invalid settings");
                return SessionMapper.CreateResult(false, ex.Message,
                    ex.InvalidKeys.Select(k => $"Invalid setting: {k}").ToList());
            }
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error starting session");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to start session", ex));
        }
    }

    public override async Task<OperationResult> StopSession(StopSessionRequest request, ServerCallContext context)
    {
        try
        {
            logger.LogInformation("Stopping current session");

            try
            {
                await sessionManager.StopCurrentSessionAsync(context.CancellationToken).ConfigureAwait(false);

                logger.LogInformation("Session stopped successfully");
                return SessionMapper.CreateResult(true, "Session stopped successfully");
            }
            catch (SessionException ex)
            {
                logger.LogWarning(ex, "Session stop failed: {Message}", ex.Message);
                return SessionMapper.CreateResult(false, ex.Message, [ex.Message]);
            }
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error stopping session");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to stop session", ex));
        }
    }

    public override async Task StreamSessionEvents(StreamSessionEventsRequest request,
        IServerStreamWriter<SessionEvent> responseStream, ServerCallContext context)
    {
        var subscriberId = Guid.NewGuid().ToString();

        try
        {
            logger.LogInformation("Client {SubscriberId} started streaming session events", subscriberId);

            await eventBroadcaster.SubscribeAsync(subscriberId, responseStream, context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Client {SubscriberId} session event stream cancelled", subscriberId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error streaming session events for {SubscriberId}", subscriberId);
            throw;
        }
    }
}