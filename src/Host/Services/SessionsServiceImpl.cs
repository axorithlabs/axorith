using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Mappers;
using Axorith.Host.Streaming;
using Axorith.Shared.Exceptions;
using Axorith.Telemetry;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Action = Axorith.Contracts.Action;
using FocusCommitmentMode = Axorith.Core.Models.FocusCommitmentMode;
using AfterEndBehavior = Axorith.Core.Models.AfterEndBehavior;

namespace Axorith.Host.Services;

/// <summary>
///     gRPC service implementation for session management.
///     Wraps Core ISessionManager and provides event streaming via SessionEventBroadcaster.
/// </summary>
public class SessionsServiceImpl(
    ISessionManager sessionManager,
    IPresetManager presetManager,
    SessionEventBroadcaster eventBroadcaster,
    ILogger<SessionsServiceImpl> logger,
    ITelemetryService? telemetry = null,
    ISessionAutoStopService? autoStopService = null,
    IScheduleManager? scheduleManager = null)
    : SessionsService.SessionsServiceBase
{
    private readonly ITelemetryService _telemetry = telemetry ?? new NoopTelemetryService();
    public override Task<SessionHistory> GetSessionHistory(GetSessionStateRequest request, ServerCallContext context)
    {
        var result = new SessionHistory();
        result.Entries.AddRange(sessionManager.SessionHistory.Select(activity => new SessionHistoryEntry
        {
            StartedAtUnixMs = activity.StartedAt.ToUnixTimeMilliseconds(),
            EndedAtUnixMs = activity.EndedAt.ToUnixTimeMilliseconds(),
            PresetName = activity.PresetName
        }));
        return Task.FromResult(result);
    }
    public override async Task<SessionState> GetSessionState(GetSessionStateRequest request, ServerCallContext context)
    {
        try
        {
            logger.LogDebug("GetSessionState called");

            var snapshot = sessionManager.GetCurrentSnapshot();

            var state = new SessionState
            {
                IsActive = snapshot != null
            };

            if (snapshot != null)
            {
                state.PresetId = snapshot.PresetId.ToString();
                state.PresetName = snapshot.PresetName;

                var activePreset = sessionManager.ActiveSession;
                var commitment = activePreset?.FocusCommitment;
                state.FocusCommitment = (Axorith.Contracts.FocusCommitmentMode)(commitment?.Mode ?? FocusCommitmentMode.Normal);
                state.BreaksRemaining = sessionManager.BreaksRemaining;
                state.BreaksTotal = commitment?.BreakCount ?? 0;
                var remaining = sessionManager.SessionTimeRemaining ?? autoStopService?.GetTimeRemaining();
                var endsAt = sessionManager.SessionEndsAt ??
                    (remaining.HasValue ? DateTimeOffset.UtcNow + remaining.Value : (DateTimeOffset?)null);
                if (commitment?.Mode == FocusCommitmentMode.Normal && scheduleManager != null)
                {
                    var now = DateTimeOffset.Now;
                    var schedules = await scheduleManager.GetSchedulesForPresetAsync(snapshot.PresetId,
                        context.CancellationToken).ConfigureAwait(false);
                    var scheduledEnd = schedules.Where(schedule => schedule.Type == ScheduleType.StopRecurring)
                        .Select(schedule => schedule.GetNextRun(now)).Where(end => end.HasValue).Min();
                    if (scheduledEnd.HasValue && (!endsAt.HasValue || scheduledEnd < endsAt))
                    {
                        endsAt = scheduledEnd;
                        remaining = scheduledEnd - now;
                    }
                }
                state.RemainingSeconds = (long)Math.Ceiling(Math.Max(0, remaining?.TotalSeconds ?? 0));
                state.BreakRemainingSeconds = (long)Math.Ceiling(sessionManager.BreakTimeRemaining?.TotalSeconds ?? 0);
                state.AfterEnd = (Axorith.Contracts.AfterEndBehavior)(commitment?.AfterEnd ?? AfterEndBehavior.DoNothing);
                state.ProtectionStatus = sessionManager.ProtectionStatus ?? "Protection active";
                state.EmergencyUnlockAvailable = commitment?.Mode is FocusCommitmentMode.Locked or FocusCommitmentMode.Strict;

                if (sessionManager.BreakEndsAt is { } breakEndsAt)
                {
                    state.BreakEndsAt = Timestamp.FromDateTimeOffset(breakEndsAt);
                }

                if (endsAt is { } sessionEndsAt)
                {
                    state.EndsAt = Timestamp.FromDateTimeOffset(sessionEndsAt);
                }

                if (sessionManager.SessionStartedAt is { } startedAt)
                {
                    state.StartedAt = Timestamp.FromDateTimeOffset(startedAt);
                }

                foreach (var module in snapshot.Modules)
                {
                    state.AppBlocking |= module.ModuleName.Contains("App Blocker", StringComparison.OrdinalIgnoreCase);
                    state.WebsiteBlocking |= module.ModuleName.Contains("Site Blocker", StringComparison.OrdinalIgnoreCase);
                    var moduleState = new ModuleInstanceState
                    {
                        InstanceId = module.InstanceId.ToString(),
                        ModuleName = module.ModuleName,
                        CustomName = module.CustomName ?? string.Empty,
                        Status = ModuleStatus.Running
                    };

                    foreach (var setting in module.Settings)
                    {
                        var protoSetting = new Setting
                        {
                            Key = setting.Key,
                            Label = setting.Label,
                            Description = setting.Description ?? string.Empty,
                            ControlType = (SettingControlType)(int)setting.ControlType,
                            Persistence = (SettingPersistence)(int)setting.Persistence,
                            IsReadOnly = setting.IsReadOnly,
                            IsVisible = setting.IsVisible,
                            ValueType = setting.ValueType,
                            StringValue = setting.ValueString
                        };

                        moduleState.Settings.Add(protoSetting);
                    }

                    foreach (var action in module.Actions)
                    {
                        var protoAction = new Action
                        {
                            Key = action.Key,
                            Label = action.Label,
                            IsEnabled = action.IsEnabled
                        };

                        moduleState.Actions.Add(protoAction);
                    }

                    state.ModuleStates.Add(moduleState);
                }
            }

            logger.LogDebug("Session active: {IsActive}", state.IsActive);
            return state;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting session state");
            throw new RpcException(new Status(StatusCode.Internal, "Failed to get session state", ex));
        }
    }

    public override async Task<OperationResult> StartSession(StartSessionRequest request, ServerCallContext context)
    {
        Guid? sessionInstanceId = Guid.TryParse(request.SessionInstanceId, out var parsedSessionInstanceId)
            ? parsedSessionInstanceId
            : null;
        try
        {
            if (!Guid.TryParse(request.PresetId, out var presetId))
            {
                TrackSessionStartFailure(sessionInstanceId ?? Guid.NewGuid(), null, "validation_failed");
                var result = SessionMapper.CreateResult(false, "Invalid preset ID",
                    [$"Could not parse preset ID: {request.PresetId}"]);
                return result;
            }

            logger.LogInformation("Starting session for preset {PresetId}", presetId);

            var preset = await presetManager.GetPresetByIdAsync(presetId, context.CancellationToken)
                .ConfigureAwait(false);

            if (preset == null)
            {
                TrackSessionStartFailure(sessionInstanceId ?? Guid.NewGuid(), presetId, "unknown");
                return SessionMapper.CreateResult(false, "Preset not found",
                    [$"No preset found with ID: {presetId}"]);
            }

            try
            {
                await sessionManager.StartSessionAsync(preset, context.CancellationToken, "manual", sessionInstanceId)
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

    private void TrackSessionStartFailure(Guid sessionInstanceId, Guid? presetId, string failureReason)
    {
        _telemetry.TrackEvent("SessionStartFailed", new Dictionary<string, object?>
        {
            ["sessionInstanceId"] = sessionInstanceId,
            ["presetId"] = presetId,
            ["startSource"] = "manual",
            ["stage"] = "rpc_request",
            ["failureReason"] = failureReason,
            ["result"] = "failed"
        });
    }

    public override async Task<OperationResult> PreflightSession(PreflightSessionRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.PresetId, out var presetId))
        {
            return SessionMapper.CreateResult(false, "Invalid Workspace ID", [$"Could not parse ID: {request.PresetId}"]);
        }

        var preset = await presetManager.GetPresetByIdAsync(presetId, context.CancellationToken).ConfigureAwait(false);
        if (preset == null)
        {
            return SessionMapper.CreateResult(false, "Workspace not found", [$"No Workspace found with ID: {presetId}"]);
        }

        try
        {
            await sessionManager.PreflightSessionAsync(preset, context.CancellationToken).ConfigureAwait(false);
            return SessionMapper.CreateResult(true, "Preflight passed. The Host can start this Workspace.");
        }
        catch (SessionException ex)
        {
            logger.LogWarning(ex, "Session preflight failed: {Message}", ex.Message);
            return SessionMapper.CreateResult(false, ex.Message, [ex.Message]);
        }
        catch (InvalidSettingsException ex)
        {
            return SessionMapper.CreateResult(false, ex.Message,
                ex.InvalidKeys.Select(key => $"Invalid setting: {key}").ToList());
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

    public override async Task<OperationResult> StartBreak(StartBreakRequest request, ServerCallContext context)
    {
        try
        {
            await sessionManager.StartBreakAsync(context.CancellationToken).ConfigureAwait(false);
            return SessionMapper.CreateResult(true, "Break started.");
        }
        catch (SessionException ex)
        {
            return SessionMapper.CreateResult(false, ex.Message, [ex.Message]);
        }
    }

    public override async Task HoldEmergencyUnlock(
        IAsyncStreamReader<EmergencyUnlockHoldSignal> requestStream,
        IServerStreamWriter<EmergencyUnlockHoldProgress> responseStream,
        ServerCallContext context)
    {
        long? holdStartedAt = null;
        long? lastSignalAt = null;

        await foreach (var signal in requestStream.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
        {
            var mode = sessionManager.ActiveSession?.FocusCommitment.Mode;
            if (mode is not (FocusCommitmentMode.Locked or FocusCommitmentMode.Strict))
            {
                await responseStream.WriteAsync(new EmergencyUnlockHoldProgress
                {
                    Message = "There is no committed session to unlock."
                }).ConfigureAwait(false);
                return;
            }

            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!signal.IsHeld)
            {
                holdStartedAt = null;
                lastSignalAt = null;
                continue;
            }

            if (!holdStartedAt.HasValue || lastSignalAt.HasValue &&
                System.Diagnostics.Stopwatch.GetElapsedTime(lastSignalAt.Value, now) > TimeSpan.FromSeconds(1))
            {
                holdStartedAt = now;
            }

            lastSignalAt = now;
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(holdStartedAt.Value, now);
            if (elapsed >= TimeSpan.FromSeconds(60))
            {
                var ended = await sessionManager.EndCommittedSessionAsync(SessionEndReason.EmergencyUnlock,
                    context.CancellationToken).ConfigureAwait(false);
                if (!ended)
                {
                    await responseStream.WriteAsync(new EmergencyUnlockHoldProgress
                    {
                        Message = "There is no committed session to unlock."
                    }).ConfigureAwait(false);
                    return;
                }

                await responseStream.WriteAsync(new EmergencyUnlockHoldProgress
                {
                    Progress = 1,
                    Completed = true,
                    Message = "Session ended by Emergency Unlock."
                }).ConfigureAwait(false);
                return;
            }

            await responseStream.WriteAsync(new EmergencyUnlockHoldProgress
            {
                Progress = Math.Clamp(elapsed.TotalSeconds / 60, 0, 1),
                Message = "Keep holding to unlock."
            }).ConfigureAwait(false);
        }
    }
}
