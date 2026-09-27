using Axorith.Core.Services.Abstractions;
using Axorith.Core.Models;
using Axorith.Sdk.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Axorith.Core.Services;

/// <summary>
///     Service for managing automatic session stop and transition to next preset.
/// </summary>
public class SessionAutoStopService(
    ISessionManager sessionManager,
    IPresetManager presetManager,
    INotifier notifier,
    ILogger<SessionAutoStopService> logger)
    : ISessionAutoStopService
{
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _naturalEndGate = new(1, 1);
    private readonly HashSet<string> _sentNotificationKeys = [];
    private DateTimeOffset _lastCleanup = DateTimeOffset.Now;
    private long _nextProtectionHealthCheck;

    private Guid? _currentSessionId;
    private Guid? _nextPresetId;
    private DateTimeOffset? _stopAt;
    private long? _stopAtTimestamp;
    private Task? _loopTask;
    private CancellationTokenSource? _loopCts;

    private volatile bool _isStoppingSession;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        sessionManager.SessionStopped += OnSessionStopped;
        return Task.CompletedTask;
    }

    public Task StartTrackingAsync(Guid sessionId, TimeSpan? autoStopDuration, Guid? nextPresetId,
        CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            _loopCts?.Cancel();
            _loopCts?.Dispose();
            _loopCts = null;
            _loopTask = null;

            _currentSessionId = sessionId;
            _nextPresetId = nextPresetId;
            _sentNotificationKeys.Clear();

            if (autoStopDuration.HasValue)
            {
                var duration = autoStopDuration.Value > TimeSpan.Zero ? autoStopDuration.Value : TimeSpan.Zero;
                _stopAt = DateTimeOffset.UtcNow + duration;
                _stopAtTimestamp = System.Diagnostics.Stopwatch.GetTimestamp() +
                                   (long)(duration.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);
                _loopCts = new CancellationTokenSource();
                _loopTask = RunTrackingLoopAsync(_loopCts.Token);

                logger.LogInformation(
                    "Started tracking session {SessionId} with auto-stop at {StopAt} (in {Duration}). Next preset: {NextPresetId}",
                    sessionId, _stopAt.Value, duration, nextPresetId?.ToString() ?? "none");
            }
            else
            {
                _stopAt = null;
                _stopAtTimestamp = null;
                logger.LogInformation("Started tracking session {SessionId} without auto-stop", sessionId);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopTrackingAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            _loopCts?.Cancel();
            _loopCts?.Dispose();
            _loopCts = null;
            _loopTask = null;

            _currentSessionId = null;
            _nextPresetId = null;
            _stopAt = null;
            _stopAtTimestamp = null;
            _sentNotificationKeys.Clear();

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
        DateTimeOffset? stopAt;
        long? stopAtTimestamp;
        Guid? nextPresetId;
        Guid? currentSessionId;
        SessionPreset? expectedSession;

        lock (_stateLock)
        {
            if (!_stopAt.HasValue || !sessionManager.IsSessionRunning)
            {
                return;
            }

            stopAt = _stopAt;
            stopAtTimestamp = _stopAtTimestamp;
            nextPresetId = _nextPresetId;
            currentSessionId = _currentSessionId;
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

        if (!stopAt.HasValue || !stopAtTimestamp.HasValue)
        {
            return;
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
                await CompleteNaturallyAsync(expectedSession, nextPresetId, ct).ConfigureAwait(false);
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
            currentSessionId = _currentSessionId;
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
        await notifier.ShowSystemAsync("Session Auto-Stop", message).ConfigureAwait(false);
    }

    public async Task<bool> CompleteNaturallyAsync(SessionPreset expectedSession, Guid? fallbackNextPresetId,
        CancellationToken cancellationToken = default)
    {
        if (!await _naturalEndGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        _isStoppingSession = true;
        try
        {
            if (!ReferenceEquals(sessionManager.ActiveSession, expectedSession))
            {
                return false;
            }

            if (!sessionManager.IsSessionRunning)
            {
                logger.LogWarning("Session already stopped, skipping auto-stop");
                await StopTrackingAsync(CancellationToken.None).ConfigureAwait(false);
                return false;
            }

            var currentPreset = expectedSession;
            var currentSessionId = _currentSessionId ?? currentPreset.Id;

            logger.LogInformation("Auto-stopping session '{PresetName}' (ID: {SessionId})",
                currentPreset.Name, currentSessionId);

            var commitment = currentPreset.FocusCommitment;
            var afterEnd = commitment.AfterEnd;
            var nextPresetId = fallbackNextPresetId;
            if (afterEnd == AfterEndBehavior.StartNextWorkspace)
            {
                nextPresetId = commitment.NextWorkspaceId ?? nextPresetId;
            }
            else if (afterEnd != AfterEndBehavior.DoNothing)
            {
                nextPresetId = null;
            }

            lock (_stateLock)
            {
                _currentSessionId = null;
                _nextPresetId = null;
                _stopAt = null;
                _stopAtTimestamp = null;
                _sentNotificationKeys.Clear();
            }

            try
            {
                var ended = await sessionManager.EndCommittedSessionAsync(SessionEndReason.NaturalCompletion,
                    CancellationToken.None).ConfigureAwait(false);
                if (!ended)
                {
                    logger.LogInformation("Session ended before its natural completion handler acquired the stop lock.");
                    return false;
                }

                logger.LogInformation("Session '{PresetName}' stopped successfully", currentPreset.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to auto-stop session '{PresetName}'", currentPreset.Name);
                await notifier.ShowSystemAsync("Auto-Stop Error",
                    $"Failed to stop session '{currentPreset.Name}': {ex.Message}").ConfigureAwait(false);
                return false;
            }
            finally
            {
                lock (_stateLock)
                {
                    _loopCts?.Cancel();
                }
            }

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
                                $"Session stopped. Next preset (ID: {nextPresetId.Value}) not found.")
                            .ConfigureAwait(false);
                        return true;
                    }

                    logger.LogInformation("Starting next preset '{NextPresetName}'", nextPreset.Name);
                    await notifier.ShowSystemAsync("Session Transition",
                        $"Starting '{nextPreset.Name}'...").ConfigureAwait(false);

                    await sessionManager.StartSessionAsync(nextPreset, CancellationToken.None).ConfigureAwait(false);

                    logger.LogInformation("Next preset '{NextPresetName}' started successfully", nextPreset.Name);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to start next preset {NextPresetId}", nextPresetId.Value);
                    await notifier.ShowSystemAsync("Auto-Stop Error",
                        $"Failed to start next preset: {ex.Message}").ConfigureAwait(false);
                }
            }
            else
            {
                await notifier.ShowSystemAsync("Session Auto-Stop",
                    $"Session '{currentPreset.Name}' has ended.").ConfigureAwait(false);
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
                $"Could not perform '{behavior}': {ex.Message}").ConfigureAwait(false);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    private TimeSpan GetRemainingTimeLocked()
    {
        if (!_stopAtTimestamp.HasValue)
        {
            return TimeSpan.Zero;
        }

        var ticks = _stopAtTimestamp.Value - System.Diagnostics.Stopwatch.GetTimestamp();
        return ticks <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds((double)ticks / System.Diagnostics.Stopwatch.Frequency);
    }

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
