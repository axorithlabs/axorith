using System.Text.Json;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk.Services;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging;

namespace Axorith.Core.Services;

public class ScheduleManager(
    string storageDirectory,
    ISessionManager sessionManager,
    IPresetManager presetManager,
    ISessionAutoStopService autoStopService,
    INotifier notifier,
    ILogger<ScheduleManager> logger)
    : IScheduleManager
{
    private readonly string _storagePath = Path.Combine(storageDirectory, "config", "schedules.json");
    private readonly string _notificationStatePath = Path.Combine(storageDirectory, "config", "notifications.json");
    private const long MaxScheduleFileSizeBytes = 5 * 1024 * 1024; // 5 MB max

    // ── Persistent notification state ────────────────────────────────────
    // Each schedule tracks which notification thresholds have been sent
    // for its current next-run-time via a bitmask. This state survives
    // app restarts so notifications are never duplicated.
    //
    // When the next-run-time changes (schedule fires or is rescheduled),
    // the bitmask resets to 0 and notifications start fresh for the new run.

    private const int Threshold5Min = 1;   // bit 0 — 5-minute warning
    private const int Threshold1Min = 2;   // bit 1 — 1-minute warning
    private const int Threshold15Sec = 4;  // bit 2 — 15-second warning

    private sealed class ScheduleNotificationState
    {
        public long NextRunTicks { get; set; }
        public int SentThresholds { get; set; }
    }

    private sealed class NotificationStateFile
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, ScheduleNotificationState> Schedules { get; set; } = new();
    }

    private NotificationStateFile _notificationState = new();
    private DateTimeOffset _lastNotificationSave = DateTimeOffset.MinValue;

    private readonly List<SessionSchedule> _schedules = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        MaxDepth = 64
    };

    private volatile bool _isProcessingSchedule;

    private Task? _loopTask;
    private CancellationTokenSource? _loopCts;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        await LoadNotificationStateAsync(cancellationToken);

        sessionManager.SessionStarted += OnSessionStarted;

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunSchedulerLoopAsync(_loopCts.Token);

        logger.LogInformation("Scheduler started with {Count} schedules", _schedules.Count);
    }

    private async void OnSessionStarted(Guid presetId)
    {
        try
        {
            var schedules = await GetSchedulesForPresetAsync(presetId, CancellationToken.None);

            var durationSchedule = schedules.FirstOrDefault(s =>
                s is { Type: ScheduleType.StopDuration, IsEnabled: true, AutoStopDuration: not null } &&
                s.AutoStopDuration.Value > TimeSpan.Zero);

            if (durationSchedule == null)
            {
                return;
            }

            logger.LogInformation(
                "Found StopDuration schedule for preset {PresetId}: duration={Duration}, nextPreset={NextPresetId}",
                presetId, durationSchedule.AutoStopDuration, durationSchedule.NextPresetId);

            await autoStopService.StartTrackingAsync(
                presetId,
                durationSchedule.AutoStopDuration,
                durationSchedule.NextPresetId,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check StopDuration schedules for preset {PresetId}", presetId);
        }
    }

    public async Task<IReadOnlyList<SessionSchedule>> ListSchedulesAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return _schedules.ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<SessionSchedule>> GetSchedulesForPresetAsync(Guid presetId,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return _schedules.Where(s => s.PresetId == presetId && s.IsEnabled).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<SessionSchedule> SaveScheduleAsync(SessionSchedule schedule, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var existing = _schedules.FirstOrDefault(s => s.Id == schedule.Id);
            if (existing != null)
            {
                _schedules.Remove(existing);
            }

            _schedules.Add(schedule);

            // Reset notification state — schedule was modified, next run may have changed
            _notificationState.Schedules.Remove(schedule.Id.ToString());

            await SaveToDiskAsync(cancellationToken);
            await SaveNotificationStateAsync(cancellationToken);
            return schedule;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteScheduleAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _schedules.RemoveAll(s => s.Id == scheduleId);
            _notificationState.Schedules.Remove(scheduleId.ToString());
            await SaveToDiskAsync(cancellationToken);
            await SaveNotificationStateAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<SessionSchedule?> SetEnabledAsync(Guid scheduleId, bool enabled,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var schedule = _schedules.FirstOrDefault(s => s.Id == scheduleId);
            if (schedule != null)
            {
                schedule.IsEnabled = enabled;
                await SaveToDiskAsync(cancellationToken);
            }

            return schedule;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task RunSchedulerLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await CheckAndRunSchedulesAsync(ct);
                await PruneOrphanedNotificationStatesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in scheduler loop");
            }
        }
    }

    private async Task CheckAndRunSchedulesAsync(CancellationToken ct)
    {
        if (_isProcessingSchedule)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        List<(SessionSchedule Schedule, DateTimeOffset RunTime)> toRun = [];
        List<(SessionSchedule Schedule, DateTimeOffset RunTime)> toStop = [];

        await _lock.WaitAsync(ct);
        try
        {
            foreach (var schedule in _schedules)
            {
                if (!schedule.IsEnabled)
                {
                    continue;
                }

                var nextRun = schedule.GetNextRun(now);

                if (!nextRun.HasValue)
                {
                    continue;
                }

                var runTime = nextRun.Value;
                var timeLeft = runTime - now;

                if (timeLeft.TotalMinutes is > 6 or < -5)
                {
                    continue;
                }

                switch (schedule.Type)
                {
                    case ScheduleType.StopRecurring:
                    {
                        if (timeLeft <= TimeSpan.FromSeconds(16) && timeLeft > TimeSpan.Zero)
                        {
                            await CheckAndNotifyWithStateAsync(schedule, runTime,
                                "15 seconds", Threshold15Sec, isStop: true, ct);
                        }

                        if (timeLeft <= TimeSpan.FromSeconds(61) && timeLeft > TimeSpan.FromSeconds(16))
                        {
                            await CheckAndNotifyWithStateAsync(schedule, runTime,
                                "1 minute", Threshold1Min, isStop: true, ct);
                        }

                        if (timeLeft <= TimeSpan.FromSeconds(301) && timeLeft > TimeSpan.FromSeconds(61))
                        {
                            await CheckAndNotifyWithStateAsync(schedule, runTime,
                                "5 minutes", Threshold5Min, isStop: true, ct);
                        }

                        if (timeLeft > TimeSpan.FromSeconds(2) || timeLeft < TimeSpan.FromSeconds(-30))
                        {
                            continue;
                        }

                        if (schedule.LastRun.HasValue && (now - schedule.LastRun.Value).TotalSeconds < 60)
                        {
                            continue;
                        }

                        toStop.Add((schedule, runTime));
                        break;
                    }
                    case ScheduleType.StopDuration:
                        // StopDuration schedules are handled by ISessionAutoStopService when session starts
                        // They don't run on a fixed time, but track duration from session start
                        break;
                    default:
                    {
                        if (sessionManager.IsSessionRunning)
                        {
                            continue;
                        }

                        if (timeLeft <= TimeSpan.FromSeconds(16) && timeLeft > TimeSpan.Zero)
                        {
                            await CheckAndNotifyWithStateAsync(schedule, runTime,
                                "15 seconds", Threshold15Sec, isStop: false, ct);
                        }

                        if (timeLeft <= TimeSpan.FromSeconds(61) && timeLeft > TimeSpan.FromSeconds(16))
                        {
                            await CheckAndNotifyWithStateAsync(schedule, runTime,
                                "1 minute", Threshold1Min, isStop: false, ct);
                        }

                        if (timeLeft <= TimeSpan.FromSeconds(301) && timeLeft > TimeSpan.FromSeconds(61))
                        {
                            await CheckAndNotifyWithStateAsync(schedule, runTime,
                                "5 minutes", Threshold5Min, isStop: false, ct);
                        }

                        if (timeLeft > TimeSpan.FromSeconds(2) || timeLeft < TimeSpan.FromSeconds(-30))
                        {
                            continue;
                        }

                        if (schedule.LastRun.HasValue && (now - schedule.LastRun.Value).TotalSeconds < 60)
                        {
                            continue;
                        }

                        toRun.Add((schedule, runTime));
                        break;
                    }
                }
            }
        }
        finally
        {
            _lock.Release();
        }

        toStop = [.. toStop.OrderBy(x => x.RunTime)];
        toRun = [.. toRun.OrderBy(x => x.RunTime)];

        foreach (var (schedule, runTime) in toStop)
        {
            if (!sessionManager.IsSessionRunning)
            {
                await UpdateLastRunAsync(schedule, now, ct);
                continue;
            }

            _isProcessingSchedule = true;
            try
            {
                logger.LogInformation("Triggering stop schedule '{Name}' for preset {PresetId} at {RunTime}",
                    schedule.Name, schedule.PresetId, runTime);

                await notifier.ShowSystemAsync("Session Scheduler", "Stopping session now...");

                await sessionManager.StopCurrentSessionAsync(ct);

                logger.LogInformation("Session stopped successfully by schedule '{Name}'", schedule.Name);

                if (schedule.NextPresetId.HasValue)
                {
                    var nextPreset = await presetManager.GetPresetByIdAsync(schedule.NextPresetId.Value, ct);
                    if (nextPreset != null)
                    {
                        logger.LogInformation("Starting next preset '{NextPresetName}' as configured in schedule",
                            nextPreset.Name);
                        await notifier.ShowSystemAsync("Session Scheduler", $"Starting '{nextPreset.Name}' now...");

                        await sessionManager.StartSessionAsync(nextPreset, ct);

                        logger.LogInformation("Next preset '{NextPresetName}' started successfully", nextPreset.Name);
                    }
                    else
                    {
                        logger.LogWarning("Next preset {NextPresetId} not found for schedule '{Name}'",
                            schedule.NextPresetId.Value, schedule.Name);
                    }
                }

                await UpdateLastRunAsync(schedule, now, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to execute stop schedule '{Name}'", schedule.Name);
                await notifier.ShowSystemAsync("Schedule Error", $"Failed to stop session: {ex.Message}");
                await UpdateLastRunAsync(schedule, now, ct);
            }
            finally
            {
                _isProcessingSchedule = false;
            }

            return;
        }

        foreach (var (schedule, runTime) in toRun)
        {
            if (sessionManager.IsSessionRunning)
            {
                logger.LogDebug("Skipping start schedule '{Name}' - session already running", schedule.Name);
                continue;
            }

            _isProcessingSchedule = true;
            try
            {
                logger.LogInformation("Triggering start schedule '{Name}' for preset {PresetId} at {RunTime}",
                    schedule.Name, schedule.PresetId, runTime);

                var preset = await presetManager.GetPresetByIdAsync(schedule.PresetId, ct);
                if (preset == null)
                {
                    logger.LogWarning("Preset {PresetId} not found for schedule '{Name}'. Disabling schedule.",
                        schedule.PresetId, schedule.Name);
                    await SetEnabledAsync(schedule.Id, false, ct);
                    continue;
                }

                await notifier.ShowSystemAsync("Session Scheduler", $"Starting '{preset.Name}' now...");

                await sessionManager.StartSessionAsync(preset, ct);

                logger.LogInformation("Session '{PresetName}' started successfully by schedule '{ScheduleName}'",
                    preset.Name, schedule.Name);

                // Note: Auto-stop tracking is automatically started via SessionStarted event handler
                // which checks for StopDuration schedules

                await UpdateLastRunAsync(schedule, now, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to execute start schedule '{Name}'", schedule.Name);
                await notifier.ShowSystemAsync("Schedule Error", $"Failed to start '{schedule.Name}': {ex.Message}");
                await UpdateLastRunAsync(schedule, now, ct);
            }
            finally
            {
                _isProcessingSchedule = false;
            }

            return;
        }
    }

    private async Task UpdateLastRunAsync(SessionSchedule schedule, DateTimeOffset now, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            schedule.LastRun = now;
            await SaveToDiskAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task CheckAndNotifyWithStateAsync(SessionSchedule schedule, DateTimeOffset runTime, string timeText, int thresholdBit, bool isStop, CancellationToken ct)
    {
        var scheduleKey = schedule.Id.ToString();

        if (!_notificationState.Schedules.TryGetValue(scheduleKey, out var state))
        {
            state = new ScheduleNotificationState();
            _notificationState.Schedules[scheduleKey] = state;
        }

        // Run time changed (new occurrence or reschedule) — reset bitmask
        if (state.NextRunTicks != runTime.Ticks)
        {
            state.NextRunTicks = runTime.Ticks;
            state.SentThresholds = 0;
        }

        // Already sent this threshold — skip
        if ((state.SentThresholds & thresholdBit) != 0)
        {
            return;
        }

        // Mark as sent and persist immediately
        state.SentThresholds |= thresholdBit;
        await SaveNotificationStateAsync(ct);

        if (isStop)
        {
            logger.LogInformation("Sending stop schedule warning: {Name} in {TimeText}", schedule.Name, timeText);
            await notifier.ShowSystemAsync("Session Scheduler", $"Session will stop in {timeText}.");
        }
        else
        {
            var preset = await presetManager.GetPresetByIdAsync(schedule.PresetId, ct);
            if (preset == null)
            {
                return;
            }

            logger.LogInformation("Sending schedule warning: {Name} in {TimeText}", schedule.Name, timeText);
            await notifier.ShowSystemAsync("Session Scheduler",
                $"Session '{preset.Name}' will start in {timeText}.");
        }
    }

    private async Task PruneOrphanedNotificationStatesAsync(CancellationToken ct)
    {
        if ((DateTimeOffset.Now - _lastNotificationSave).TotalHours < 1)
        {
            return;
        }

        _lastNotificationSave = DateTimeOffset.Now;

        var existingIds = new HashSet<string>(_schedules.Select(s => s.Id.ToString()));
        var keysToRemove = _notificationState.Schedules.Keys
            .Where(k => !existingIds.Contains(k))
            .ToList();

        if (keysToRemove.Count > 0)
        {
            foreach (var key in keysToRemove)
            {
                _notificationState.Schedules.Remove(key);
            }

            await SaveNotificationStateAsync(ct);
            logger.LogDebug("Pruned {Count} orphaned notification states", keysToRemove.Count);
        }
    }

    private async Task LoadNotificationStateAsync(CancellationToken ct)
    {
        if (!File.Exists(_notificationStatePath))
        {
            return;
        }

        try
        {
            var fileInfo = new FileInfo(_notificationStatePath);
            if (fileInfo.Length > MaxScheduleFileSizeBytes)
            {
                logger.LogWarning("Notification state file {Path} exceeds size limit ({Size} bytes)",
                    TelemetryGuard.SafePath(_notificationStatePath), fileInfo.Length);
                return;
            }

            await using var stream = File.OpenRead(_notificationStatePath);
            var loaded = await JsonSerializer.DeserializeAsync<NotificationStateFile>(stream, _jsonOptions, ct);
            if (loaded != null)
            {
                _notificationState = loaded;
                logger.LogDebug("Loaded notification state for {Count} schedules",
                    _notificationState.Schedules.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load notification state — starting with empty state");
            _notificationState = new NotificationStateFile();
        }
    }

    private async Task SaveNotificationStateAsync(CancellationToken ct)
    {
        try
        {
            var dir = Path.GetDirectoryName(_notificationStatePath);
            if (dir != null)
            {
                Directory.CreateDirectory(dir);
            }

            // Atomic write: write to temp file, then rename
            // Prevents corruption if app crashes mid-write
            var tempPath = _notificationStatePath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, _notificationState, _jsonOptions, ct);
            }

            File.Move(tempPath, _notificationStatePath, overwrite: true);
            _lastNotificationSave = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save notification state");
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_storagePath))
        {
            return;
        }

        try
        {
            var fileInfo = new FileInfo(_storagePath);
            if (fileInfo.Length > MaxScheduleFileSizeBytes)
            {
                logger.LogWarning("Schedule file {Path} exceeds maximum size limit ({Size} bytes)",
                    TelemetryGuard.SafePath(_storagePath),
                    fileInfo.Length);
                return;
            }

            await using var stream = File.OpenRead(_storagePath);
            // V5611: System.Text.Json is safe - no polymorphic deserialization or type name handling
            // File size and MaxDepth are validated to prevent DoS attacks
            var loaded =
                await JsonSerializer.DeserializeAsync<List<SessionSchedule>>(stream, _jsonOptions, ct); //-V5611
            if (loaded != null)
            {
                _schedules.Clear();
                _schedules.AddRange(loaded);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load schedules from {Path}", TelemetryGuard.SafePath(_storagePath));
        }
    }

    private async Task SaveToDiskAsync(CancellationToken ct)
    {
        try
        {
            var dir = Path.GetDirectoryName(_storagePath);
            if (dir != null)
            {
                Directory.CreateDirectory(dir);
            }

            await using var stream = File.Create(_storagePath);
            await JsonSerializer.SerializeAsync(stream, _schedules, _jsonOptions, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save schedules to {Path}", TelemetryGuard.SafePath(_storagePath));
        }
    }

    public async ValueTask DisposeAsync()
    {
        sessionManager.SessionStarted -= OnSessionStarted;

        _loopCts?.Cancel();
        if (_loopTask != null)
        {
            try
            {
                await _loopTask;
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