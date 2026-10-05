using Axorith.Core.Services.Abstractions;
using Axorith.Core.Models;
using Axorith.Core.Telemetry;
using Axorith.Sdk.Services;
using Axorith.Shared.Utils;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Axorith.Core.Services;

public class SessionAutoStopService(
    ISessionManager sessionManager,
    IPresetManager presetManager,
    INotifier notifier,
    ILogger<SessionAutoStopService> logger,
    ITelemetryService? telemetry = null)
    : ISessionAutoStopService
{
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _naturalEndGate = new(1, 1);
    private readonly HashSet<string> _sentNotificationKeys = [];
    private DateTimeOffset _lastCleanup = DateTimeOffset.Now;
    private long _nextProtectionHealthCheck;

    private Guid? _currentSessionInstanceId;
    private Guid? _nextPresetId;
    private DateTimeOffset? _stopAt;
    private long? _stopAtTimestamp;
    private SessionSchedule? _stopSchedule;
    private readonly ITelemetryService _telemetry = telemetry ?? NoopTelemetryService.Instance;
    private Task? _loopTask;
    private CancellationTokenSource? _loopCts;

    private volatile bool _isStoppingSession;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        sessionManager.SessionStopped += OnSessionStopped;
        return Task.CompletedTask;
    }

    public Task StartTrackingAsync(Guid sessionInstanceId, TimeSpan? autoStopDuration, Guid? nextPresetId,
        CancellationToken cancellationToken = default, SessionSchedule? schedule = null)
    {
        lock (_stateLock)
        {
            StopTrackingLoopLocked();
            _currentSessionInstanceId = sessionInstanceId;
            _nextPresetId = nextPresetId;
            _stopSchedule = schedule;
            _sentNotificationKeys.Clear();

            if (autoStopDuration.HasValue)
            {
                var duration = autoStopDuration.Value > TimeSpan.Zero ? autoStopDuration.Value : TimeSpan.Zero;
                _stopAt = DateTimeOffset.UtcNow + duration;
                _stopAtTimestamp = MonotonicTime.DeadlineAfter(duration);
                _loopCts = new CancellationTokenSource();
                _loopTask = RunTrackingLoopAsync(_loopCts.Token);

                logger.LogInformation(
                    "Started tracking session {SessionId} with auto-stop at {StopAt} (in {Duration}). Next preset: {NextPresetId}",
                    sessionInstanceId, _stopAt.Value, duration, nextPresetId?.ToString() ?? "none");
            }
            else
            {
                _stopAt = null;
                _stopAtTimestamp = null;
                logger.LogInformation("Started tracking session instance {SessionInstanceId} without auto-stop",
                    sessionInstanceId);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopTrackingAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            StopTrackingLoopLocked();
            ClearTrackingStateLocked();
            logger.LogDebug("Stopped tracking session");
        }

        return Task.CompletedTask;
    }

    public TimeSpan? GetTimeRemaining()
    {
        lock (_stateLock)
        {
            if (!_stopAt.HasValue)
            {
                return null;
            }

            return GetRemainingTimeLocked();
        }
    }

    private async void OnSessionStopped(Guid sessionId)
    {
        if (_isStoppingSession)
        {
            return;
        }

        try
        {
            await StopTrackingAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Error while stopping tracking after session stopped");
        }
    }

    private async Task RunTrackingLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));

            while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    await CheckAndProcessAsync(ct).ConfigureAwait(false);
                    CleanupNotificationCache();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error in auto-stop tracking loop");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    private async Task CheckAndProcessAsync(CancellationToken ct)
    {
        Guid? nextPresetId;
        SessionSchedule? stopSchedule;
        SessionPreset? expectedSession;

        lock (_stateLock)
        {
            if (!_stopAt.HasValue || !sessionManager.IsSessionRunning)
            {
                return;
            }

            nextPresetId = _nextPresetId;
            stopSchedule = _stopSchedule;
            expectedSession = sessionManager.ActiveSession;
        }

        if (sessionManager.BreakTimeRemaining is { } breakRemaining && breakRemaining <= TimeSpan.Zero)
        {
            try
            {
                await sessionManager.EndBreakAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to restore blockers after a committed session break.");
            }
        }

        var nowTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        if (nowTimestamp >= Interlocked.Read(ref _nextProtectionHealthCheck))
        {
            Interlocked.Exchange(ref _nextProtectionHealthCheck,
                nowTimestamp + System.Diagnostics.Stopwatch.Frequency * 5);
            await sessionManager.RefreshProtectionHealthAsync(ct).ConfigureAwait(false);
        }

        TimeSpan timeLeft;
        lock (_stateLock)
        {
            timeLeft = GetRemainingTimeLocked();
        }

        if (timeLeft <= TimeSpan.Zero)
        {
            if (expectedSession != null)
            {
                await CompleteNaturallyAsync(expectedSession, nextPresetId, ct, stopSchedule).ConfigureAwait(false);
            }
            return;
        }

        await SendNotificationsIfNeededAsync(timeLeft, ct).ConfigureAwait(false);
    }

    private async Task SendNotificationsIfNeededAsync(TimeSpan timeLeft, CancellationToken ct)
    {
        if (timeLeft <= TimeSpan.FromSeconds(15) && timeLeft > TimeSpan.FromSeconds(10))
        {
            await TrySendNotificationAsync(TimeSpan.FromSeconds(15), "15 seconds", ct).ConfigureAwait(false);
        }
        else if (timeLeft <= TimeSpan.FromMinutes(1) && timeLeft > TimeSpan.FromSeconds(55))
        {
            await TrySendNotificationAsync(TimeSpan.FromMinutes(1), "1 minute", ct).ConfigureAwait(false);
        }
        else if (timeLeft <= TimeSpan.FromMinutes(5) && timeLeft > TimeSpan.FromMinutes(4.9))
        {
            await TrySendNotificationAsync(TimeSpan.FromMinutes(5), "5 minutes", ct).ConfigureAwait(false);
        }
        else if (timeLeft <= TimeSpan.FromMinutes(15) && timeLeft > TimeSpan.FromMinutes(14.9))
        {
            await TrySendNotificationAsync(TimeSpan.FromMinutes(15), "15 minutes", ct).ConfigureAwait(false);
        }
    }

    private async Task TrySendNotificationAsync(TimeSpan threshold, string timeText, CancellationToken ct)
    {
        Guid? currentSessionId;
        long? stopAtTicks;
        Guid? nextPresetIdLocal;

        lock (_stateLock)
        {
            currentSessionId = _currentSessionInstanceId;
            stopAtTicks = _stopAt?.Ticks;
            nextPresetIdLocal = _nextPresetId;
        }

        var key = $"{currentSessionId}_{stopAtTicks}_{threshold.TotalSeconds}";

        lock (_stateLock)
        {
            if (!_sentNotificationKeys.Add(key))
            {
                return;
            }
        }

        var preset = sessionManager.ActiveSession;
        if (preset == null)
        {
            return;
        }

        string message;
        if (nextPresetIdLocal.HasValue)
        {
            var nextPreset = await presetManager.GetPresetByIdAsync(nextPresetIdLocal.Value, ct).ConfigureAwait(false);
            var nextPresetName = nextPreset?.Name ?? "next session";
            message = $"Session '{preset.Name}' will end in {timeText}, then '{nextPresetName}' will start.";
        }
        else
        {
            message = $"Session '{preset.Name}' will end in {timeText}.";
        }

        logger.LogInformation("Sending auto-stop warning: {Message}", message);
        await notifier.ShowSystemAsync("Session Auto-Stop", message, category: "Session Auto-Stop").ConfigureAwait(false);
    }

    public async Task<bool> CompleteNaturallyAsync(SessionPreset expectedSession, Guid? fallbackNextPresetId,
        CancellationToken cancellationToken = default, SessionSchedule? schedule = null)
    {
        if (!await _naturalEndGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            TrackScheduleTriggered(schedule, "skipped", skipReason: "concurrent_stop");
            return false;
        }

        _isStoppingSession = true;
        try
        {
            if (!ReferenceEquals(sessionManager.ActiveSession, expectedSession))
            {
                TrackScheduleTriggered(schedule, "skipped", skipReason: "session_changed");
                return false;
            }

            if (!sessionManager.IsSessionRunning)
            {
                logger.LogWarning("Session already stopped, skipping auto-stop");
                await StopTrackingAsync(CancellationToken.None).ConfigureAwait(false);
                TrackScheduleTriggered(schedule, "skipped", skipReason: "session_not_running");
                return false;
            }

            var currentPreset = expectedSession;
            Guid? trackedSessionInstanceId;
            lock (_stateLock)
            {
                trackedSessionInstanceId = _currentSessionInstanceId;
            }
            var currentSessionInstanceId = trackedSessionInstanceId ?? sessionManager.CurrentSessionInstanceId;

            logger.LogInformation("Auto-stopping session '{PresetName}' (instance: {SessionInstanceId})",
                currentPreset.Name, currentSessionInstanceId);

            var commitment = currentPreset.FocusCommitment;
            var afterEnd = commitment.AfterEnd;
            var nextPresetId = afterEnd switch
            {
                AfterEndBehavior.StartNextWorkspace => commitment.NextWorkspaceId ?? fallbackNextPresetId,
                AfterEndBehavior.DoNothing => fallbackNextPresetId,
                _ => null
            };

            lock (_stateLock)
            {
                ClearTrackingStateLocked();
            }

            try
            {
                var ended = await sessionManager.EndCommittedSessionAsync(SessionEndReason.NaturalCompletion,
                    CancellationToken.None).ConfigureAwait(false);
                if (!ended)
                {
                    logger.LogInformation("Session ended before its natural completion handler acquired the stop lock.");
                    TrackScheduleTriggered(schedule, "skipped", sessionInstanceId: currentSessionInstanceId,
                        skipReason: "session_ended_before_stop");
                    return false;
                }

                logger.LogInformation("Session '{PresetName}' stopped successfully", currentPreset.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to auto-stop session '{PresetName}'", currentPreset.Name);
                await notifier.ShowSystemAsync("Auto-Stop Error",
                    $"Failed to stop session '{currentPreset.Name}': {ex.Message}", category: "Session Auto-Stop").ConfigureAwait(false);
                TrackScheduleTriggered(schedule, "failed", sessionInstanceId: currentSessionInstanceId,
                    failureReason: ProductAnalyticsProperties.FailureReason(ex));
                return false;
            }
            finally
            {
                lock (_stateLock)
                {
                    _loopCts?.Cancel();
                }
            }

            TrackScheduleTriggered(schedule, "completed", sessionInstanceId: currentSessionInstanceId);
            if (nextPresetId.HasValue)
            {
                try
                {
                    var nextPreset = await presetManager.GetPresetByIdAsync(nextPresetId.Value, CancellationToken.None)
                        .ConfigureAwait(false);

                    if (nextPreset == null)
                    {
                        logger.LogWarning("Next preset {NextPresetId} not found", nextPresetId.Value);
                        await notifier.ShowSystemAsync("Auto-Stop",
                                $"Session stopped. Next preset (ID: {nextPresetId.Value}) not found.", category: "Session Auto-Stop")
                            .ConfigureAwait(false);
                        return true;
                    }

                    logger.LogInformation("Starting next preset '{NextPresetName}'", nextPreset.Name);
                    await notifier.ShowSystemAsync("Session Transition",
                        $"Starting '{nextPreset.Name}'...", category: "Session Auto-Stop").ConfigureAwait(false);

                    await sessionManager.StartSessionAsync(nextPreset, CancellationToken.None, startSource: "chained",
                        previousSessionInstanceId: currentSessionInstanceId).ConfigureAwait(false);

                    logger.LogInformation("Next preset '{NextPresetName}' started successfully", nextPreset.Name);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to start next preset {NextPresetId}", nextPresetId.Value);
                    await notifier.ShowSystemAsync("Auto-Stop Error",
                        $"Failed to start next preset: {ex.Message}", category: "Session Auto-Stop").ConfigureAwait(false);
                }
            }
            else
            {
                await notifier.ShowSystemAsync("Session Auto-Stop",
                    $"Session '{currentPreset.Name}' has ended.", category: "Session Auto-Stop").ConfigureAwait(false);
                await ExecuteAfterEndActionAsync(afterEnd).ConfigureAwait(false);
            }

            return true;
        }
        finally
        {
            _isStoppingSession = false;
            _naturalEndGate.Release();
        }
    }

    private void TrackScheduleTriggered(SessionSchedule? schedule, string result, Guid? sessionInstanceId = null,
        string? failureReason = null, string? skipReason = null)
    {
        if (schedule is null) return;
        _telemetry.TrackEvent("ScheduleTriggered", ProductAnalyticsProperties.ScheduleTriggered(schedule,
            "stop", result, sessionInstanceId ?? sessionManager.CurrentSessionInstanceId, failureReason, skipReason));
    }


    private void CleanupNotificationCache()
    {
        if ((DateTimeOffset.Now - _lastCleanup).TotalHours < 1)
        {
            return;
        }

        lock (_stateLock)
        {
            _sentNotificationKeys.Clear();
        }

        _lastCleanup = DateTimeOffset.Now;
    }

    public async Task ExecuteAfterEndActionAsync(AfterEndBehavior behavior)
    {
        if (behavior is AfterEndBehavior.DoNothing or AfterEndBehavior.StartNextWorkspace)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            logger.LogWarning("After-end action {Action} requires Windows and was skipped.", behavior);
            return;
        }

        try
        {
            if (behavior == AfterEndBehavior.LockPc)
            {
                if (!LockWorkStation()) throw new Win32Exception(Marshal.GetLastWin32Error());
                return;
            }

            if (behavior == AfterEndBehavior.Sleep)
            {
                if (!SetSuspendState(false, false, false)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return;
            }

            var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"))
            {
                UseShellExecute = false
            };
            var arguments = behavior == AfterEndBehavior.SignOut ? new[] { "/l" } : new[] { "/s", "/t", "0" };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo)?.Dispose();
            logger.LogInformation("Executed after-end action {Action}.", behavior);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute after-end action {Action}.", behavior);
            await notifier.ShowSystemAsync("After-Session Action Failed",
                $"Could not perform '{behavior}': {ex.Message}", category: "Session Auto-Stop").ConfigureAwait(false);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    private void StopTrackingLoopLocked()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;
        _loopTask = null;
    }

    private void ClearTrackingStateLocked()
    {
        _currentSessionInstanceId = null;
        _nextPresetId = null;
        _stopAt = null;
        _stopAtTimestamp = null;
        _stopSchedule = null;
        _sentNotificationKeys.Clear();
    }

    private TimeSpan GetRemainingTimeLocked() => _stopAtTimestamp is { } deadline
        ? MonotonicTime.RemainingUntil(deadline)
        : TimeSpan.Zero;

    public async ValueTask DisposeAsync()
    {
        sessionManager.SessionStopped -= OnSessionStopped;

        CancellationTokenSource? cts;
        Task? loopTask;

        lock (_stateLock)
        {
            cts = _loopCts;
            loopTask = _loopTask;
            _loopCts = null;
            _loopTask = null;
        }

        cts?.Cancel();

        if (loopTask != null)
        {
            try
            {
                await loopTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore cancellation
            }
        }

        cts?.Dispose();
        _naturalEndGate.Dispose();
    }
}
