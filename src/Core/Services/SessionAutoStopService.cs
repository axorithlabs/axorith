using Axorith.Core.Services.Abstractions;
using Axorith.Sdk.Services;
using Microsoft.Extensions.Logging;

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
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly HashSet<string> _sentNotificationKeys = [];
    private DateTimeOffset _lastCleanup = DateTimeOffset.Now;

    private Guid? _currentSessionId;
    private Guid? _nextPresetId;
    private DateTimeOffset? _stopAt;
    private Task? _loopTask;
    private CancellationTokenSource? _loopCts;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        sessionManager.SessionStarted += OnSessionStarted;
        sessionManager.SessionStopped += OnSessionStopped;

        logger.LogInformation("SessionAutoStopService started");
    }

    public async Task StartTrackingAsync(Guid sessionId, TimeSpan? autoStopDuration, Guid? nextPresetId,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_loopTask != null)
            {
                _loopCts?.Cancel();
                try
                {
                    await _loopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }

                _loopCts?.Dispose();
            }

            _currentSessionId = sessionId;
            _nextPresetId = nextPresetId;

            if (autoStopDuration.HasValue && autoStopDuration.Value > TimeSpan.Zero)
            {
                _stopAt = DateTimeOffset.UtcNow + autoStopDuration.Value;
                _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _loopTask = RunTrackingLoopAsync(_loopCts.Token);

                logger.LogInformation(
                    "Started tracking session {SessionId} with auto-stop in {Duration}. Next preset: {NextPresetId}",
                    sessionId, autoStopDuration, nextPresetId?.ToString() ?? "none");
            }
            else
            {
                _stopAt = null;
                logger.LogInformation("Started tracking session {SessionId} without auto-stop", sessionId);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StopTrackingAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_loopTask != null)
            {
                _loopCts?.Cancel();
                try
                {
                    await _loopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }

                _loopCts?.Dispose();
                _loopTask = null;
            }

            _currentSessionId = null;
            _nextPresetId = null;
            _stopAt = null;

            logger.LogInformation("Stopped tracking session");
        }
        finally
        {
            _lock.Release();
        }
    }

    public TimeSpan? GetTimeRemaining()
    {
        if (!_stopAt.HasValue)
        {
            return null;
        }

        var remaining = _stopAt.Value - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private void OnSessionStarted(Guid sessionId)
    {
        // Session started - tracking will be started via StartTrackingAsync
        // This is called by ScheduleManager or other code that starts sessions
    }

    private async void OnSessionStopped(Guid sessionId)
    {
        await StopTrackingAsync().ConfigureAwait(false);
    }

    private async Task RunTrackingLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await CheckAndNotifyAsync(ct).ConfigureAwait(false);
                CleanupNotificationCache();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in auto-stop tracking loop");
            }
        }
    }

    private async Task CheckAndNotifyAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        var lockReleased = false;
        try
        {
            if (!_stopAt.HasValue || !sessionManager.IsSessionRunning)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var timeLeft = _stopAt.Value - now;

            if (timeLeft <= TimeSpan.Zero)
            {
                _lock.Release();
                lockReleased = true;
                await StopSessionAndStartNextAsync(ct).ConfigureAwait(false);
                return;
            }

            if (timeLeft <= TimeSpan.FromSeconds(15) && timeLeft > TimeSpan.Zero)
            {
                await CheckAndNotifyAsync(timeLeft, TimeSpan.FromSeconds(15), "15 seconds", ct)
                    .ConfigureAwait(false);
            }
            else if (timeLeft <= TimeSpan.FromMinutes(1) && timeLeft > TimeSpan.Zero)
            {
                await CheckAndNotifyAsync(timeLeft, TimeSpan.FromMinutes(1), "1 minute", ct)
                    .ConfigureAwait(false);
            }
            else if (timeLeft <= TimeSpan.FromMinutes(5) && timeLeft > TimeSpan.Zero)
            {
                await CheckAndNotifyAsync(timeLeft, TimeSpan.FromMinutes(5), "5 minutes", ct)
                    .ConfigureAwait(false);
            }
            else if (timeLeft <= TimeSpan.FromMinutes(15) && timeLeft > TimeSpan.FromMinutes(5))
            {
                await CheckAndNotifyAsync(timeLeft, TimeSpan.FromMinutes(15), "15 minutes", ct)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            if (!lockReleased)
            {
                _lock.Release();
            }
        }
    }

    private async Task CheckAndNotifyAsync(TimeSpan timeLeft, TimeSpan threshold, string timeText,
        CancellationToken ct)
    {
        var key = $"{_currentSessionId}_{_stopAt?.Ticks}_{threshold.TotalSeconds}";

        if (!_sentNotificationKeys.Add(key))
        {
            return;
        }

        var preset = sessionManager.ActiveSession;
        if (preset == null)
        {
            return;
        }

        string message;
        if (_nextPresetId.HasValue)
        {
            var nextPreset = await presetManager.GetPresetByIdAsync(_nextPresetId.Value, ct)
                .ConfigureAwait(false);
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

    private async Task StopSessionAndStartNextAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!sessionManager.IsSessionRunning)
            {
                logger.LogWarning("Session already stopped, skipping auto-stop");
                await StopTrackingAsync(ct).ConfigureAwait(false);
                return;
            }

            var currentPreset = sessionManager.ActiveSession;
            if (currentPreset == null)
            {
                logger.LogWarning("No active session found, skipping auto-stop");
                await StopTrackingAsync(ct).ConfigureAwait(false);
                return;
            }

            logger.LogInformation("Auto-stopping session '{PresetName}'", currentPreset.Name);

            try
            {
                await sessionManager.StopCurrentSessionAsync(ct).ConfigureAwait(false);
                logger.LogInformation("Session '{PresetName}' stopped successfully", currentPreset.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to auto-stop session '{PresetName}'", currentPreset.Name);
                await notifier.ShowSystemAsync("Auto-Stop Error",
                    $"Failed to stop session '{currentPreset.Name}': {ex.Message}").ConfigureAwait(false);
                await StopTrackingAsync(ct).ConfigureAwait(false);
                return;
            }

            if (_nextPresetId.HasValue)
            {
                try
                {
                    var nextPreset = await presetManager.GetPresetByIdAsync(_nextPresetId.Value, ct)
                        .ConfigureAwait(false);

                    if (nextPreset == null)
                    {
                        logger.LogWarning("Next preset {NextPresetId} not found", _nextPresetId.Value);
                        await notifier.ShowSystemAsync("Auto-Stop Error",
                            $"Next preset (ID: {_nextPresetId.Value}) not found.").ConfigureAwait(false);
                        await StopTrackingAsync(ct).ConfigureAwait(false);
                        return;
                    }

                    logger.LogInformation("Starting next preset '{NextPresetName}'", nextPreset.Name);
                    await sessionManager.StartSessionAsync(nextPreset, ct).ConfigureAwait(false);
                    await notifier.ShowSystemAsync("Session Transition",
                        $"Starting '{nextPreset.Name}'...").ConfigureAwait(false);
                    logger.LogInformation("Next preset '{NextPresetName}' started successfully", nextPreset.Name);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to start next preset {NextPresetId}", _nextPresetId.Value);
                    await notifier.ShowSystemAsync("Auto-Stop Error",
                        $"Failed to start next preset: {ex.Message}").ConfigureAwait(false);
                }
            }

            await StopTrackingAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void CleanupNotificationCache()
    {
        if ((DateTimeOffset.Now - _lastCleanup).TotalHours < 1)
        {
            return;
        }

        _sentNotificationKeys.Clear();
        _lastCleanup = DateTimeOffset.Now;
    }

    public async ValueTask DisposeAsync()
    {
        sessionManager.SessionStarted -= OnSessionStarted;
        sessionManager.SessionStopped -= OnSessionStopped;

        _loopCts?.Cancel();
        if (_loopTask != null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore cancellation
            }
        }

        _loopCts?.Dispose();
        _lock.Dispose();
    }
}