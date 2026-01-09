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
    private readonly object _stateLock = new();
    private readonly HashSet<string> _sentNotificationKeys = [];
    private DateTimeOffset _lastCleanup = DateTimeOffset.Now;

    private Guid? _currentSessionId;
    private Guid? _nextPresetId;
    private DateTimeOffset? _stopAt;
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

            if (autoStopDuration.HasValue && autoStopDuration.Value > TimeSpan.Zero)
            {
                _stopAt = DateTimeOffset.UtcNow + autoStopDuration.Value;
                _loopCts = new CancellationTokenSource();
                _loopTask = RunTrackingLoopAsync(_loopCts.Token);

                logger.LogInformation(
                    "Started tracking session {SessionId} with auto-stop at {StopAt} (in {Duration}). Next preset: {NextPresetId}",
                    sessionId, _stopAt.Value, autoStopDuration, nextPresetId?.ToString() ?? "none");
            }
            else
            {
                _stopAt = null;
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

            var remaining = _stopAt.Value - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
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
        Guid? nextPresetId;
        Guid? currentSessionId;

        lock (_stateLock)
        {
            if (!_stopAt.HasValue || !sessionManager.IsSessionRunning)
            {
                return;
            }

            stopAt = _stopAt;
            nextPresetId = _nextPresetId;
            currentSessionId = _currentSessionId;
        }

        var now = DateTimeOffset.UtcNow;
        var timeLeft = stopAt.Value - now;

        if (timeLeft <= TimeSpan.FromSeconds(1))
        {
            await StopSessionAndStartNextAsync(currentSessionId, nextPresetId, ct).ConfigureAwait(false);
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

    private async Task StopSessionAndStartNextAsync(Guid? currentSessionId, Guid? nextPresetId, CancellationToken ct)
    {
        if (_isStoppingSession)
        {
            return;
        }

        _isStoppingSession = true;
        try
        {
            if (!sessionManager.IsSessionRunning)
            {
                logger.LogWarning("Session already stopped, skipping auto-stop");
                await StopTrackingAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            }

            var currentPreset = sessionManager.ActiveSession;
            if (currentPreset == null)
            {
                logger.LogWarning("No active session found, skipping auto-stop");
                await StopTrackingAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            }

            logger.LogInformation("Auto-stopping session '{PresetName}' (ID: {SessionId})",
                currentPreset.Name, currentSessionId);

            lock (_stateLock)
            {
                _currentSessionId = null;
                _nextPresetId = null;
                _stopAt = null;
                _sentNotificationKeys.Clear();
            }

            try
            {
                await sessionManager.StopCurrentSessionAsync(CancellationToken.None).ConfigureAwait(false);
                logger.LogInformation("Session '{PresetName}' stopped successfully", currentPreset.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to auto-stop session '{PresetName}'", currentPreset.Name);
                await notifier.ShowSystemAsync("Auto-Stop Error",
                    $"Failed to stop session '{currentPreset.Name}': {ex.Message}").ConfigureAwait(false);
                return;
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
                            $"Session stopped. Next preset (ID: {nextPresetId.Value}) not found.").ConfigureAwait(false);
                        return;
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
            }
        }
        finally
        {
            _isStoppingSession = false;
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
    }
}
