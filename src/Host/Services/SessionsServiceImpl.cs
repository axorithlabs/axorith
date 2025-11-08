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
public class SessionsServiceImpl : SessionsService.SessionsServiceBase
{
    private readonly ISessionManager _sessionManager;
    private readonly IPresetManager _presetManager;
    private readonly SessionEventBroadcaster _eventBroadcaster;
    private readonly ILogger<SessionsServiceImpl> _logger;

    public SessionsServiceImpl(ISessionManager sessionManager, IPresetManager presetManager,
        SessionEventBroadcaster eventBroadcaster, ILogger<SessionsServiceImpl> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _presetManager = presetManager ?? throw new ArgumentNullException(nameof(presetManager));
        _eventBroadcaster = eventBroadcaster ?? throw new ArgumentNullException(nameof(eventBroadcaster));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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
                // TODO: Add module states when SessionManager exposes active modules
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