using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Mappers;
using Axorith.Host.Streaming;
using Axorith.Shared.Exceptions;
using Grpc.Core;

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
    private readonly ISessionManager _sessionManager =
        sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));

    private readonly IPresetManager _presetManager =
        presetManager ?? throw new ArgumentNullException(nameof(presetManager));
    
    private readonly IModuleRegistry _moduleRegistry =
        moduleRegistry ?? throw new ArgumentNullException(nameof(moduleRegistry));

    private readonly SessionEventBroadcaster _eventBroadcaster =
        eventBroadcaster ?? throw new ArgumentNullException(nameof(eventBroadcaster));

    private readonly ILogger<SessionsServiceImpl> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public override Task<SessionState> GetSessionState(GetSessionStateRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogDebug("GetSessionState called");

            var activePreset = _sessionManager.ActiveSession;

            var state = new SessionState
            {
                IsActive = activePreset != null
            };

            if (activePreset != null)
            {
                state.PresetId = activePreset.Id.ToString();
                state.PresetName = activePreset.Name;
                state.StartedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
                
                // Populate module states from active session
                foreach (var configuredModule in activePreset.Modules)
                {
                    var instance = _sessionManager.GetActiveModuleInstance(configuredModule.ModuleId);
                    if (instance != null)
                    {
                        // Get module definition from registry
                        var definition = _moduleRegistry.GetDefinitionById(configuredModule.ModuleId);
                        
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
                                ControlType = (Contracts.SettingControlType)((int)setting.ControlType),
                                Persistence = (Contracts.SettingPersistence)((int)setting.Persistence),
                                IsReadOnly = setting.GetCurrentReadOnly(),
                                IsVisible = setting.GetCurrentVisibility(),
                                ValueType = setting.ValueType.Name,
                                StringValue = setting.GetValueAsString() ?? string.Empty
                            };
                            
                            moduleState.Settings.Add(protoSetting);
                        }
                        
                        // Add actions (convert IAction to proto Action)
                        foreach (var action in instance.GetActions())
                        {
                            var protoAction = new Contracts.Action
                            {
                                Key = action.Key
                            };
                            
                            // Get current label from Observable
                            var currentLabel = string.Empty;
                            action.Label.Subscribe(l => currentLabel = l).Dispose();
                            protoAction.Label = currentLabel;
                            
                            // Get current enabled state from Observable
                            var currentEnabled = false;
                            action.IsEnabled.Subscribe(e => currentEnabled = e).Dispose();
                            protoAction.IsEnabled = currentEnabled;
                            
                            moduleState.Actions.Add(protoAction);
                        }
                        
                        state.ModuleStates.Add(moduleState);
                    }
                }
            }

            _logger.LogDebug("Session active: {IsActive}", state.IsActive);
            return Task.FromResult(state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session state");
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

            _logger.LogInformation("Starting session for preset {PresetId}", presetId);

            // Load preset by ID directly
            var preset = await _presetManager.GetPresetByIdAsync(presetId, context.CancellationToken)
                .ConfigureAwait(false);

            if (preset == null)
                return SessionMapper.CreateResult(false, "Preset not found",
                    [$"No preset found with ID: {presetId}"]);

            try
            {
                await _sessionManager.StartSessionAsync(preset, context.CancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInformation("Session started successfully: {PresetId}", presetId);
                return SessionMapper.CreateResult(true, "Session started successfully");
            }
            catch (SessionException ex)
            {
                _logger.LogWarning(ex, "Session start failed: {Message}", ex.Message);
                return SessionMapper.CreateResult(false, ex.Message, [ex.Message]);
            }
            catch (InvalidSettingsException ex)
            {
                _logger.LogWarning(ex, "Session start failed due to invalid settings");
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
            _logger.LogError(ex, "Error starting session");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to start session", ex));
        }
    }

    public override async Task<OperationResult> StopSession(StopSessionRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Stopping current session");

            try
            {
                await _sessionManager.StopCurrentSessionAsync(context.CancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Session stopped successfully");
                return SessionMapper.CreateResult(true, "Session stopped successfully");
            }
            catch (SessionException ex)
            {
                _logger.LogWarning(ex, "Session stop failed: {Message}", ex.Message);
                return SessionMapper.CreateResult(false, ex.Message, [ex.Message]);
            }
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping session");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to stop session", ex));
        }
    }

    public override async Task StreamSessionEvents(StreamSessionEventsRequest request,
        IServerStreamWriter<SessionEvent> responseStream, ServerCallContext context)
    {
        var subscriberId = Guid.NewGuid().ToString();

        try
        {
            _logger.LogInformation("Client {SubscriberId} started streaming session events", subscriberId);

            // Delegate to broadcaster - this will block until cancellation
            await _eventBroadcaster.SubscribeAsync(subscriberId, responseStream, context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Client {SubscriberId} session event stream cancelled", subscriberId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming session events for {SubscriberId}", subscriberId);
            throw;
        }
    }
}