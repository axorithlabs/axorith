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
    private const long MaxScheduleFileSizeBytes = 5 * 1024 * 1024; // 5 MB max

    private readonly List<SessionSchedule> _schedules = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        MaxDepth = 64
    };

    private readonly HashSet<string> _sentNotificationKeys = [];
    private DateTimeOffset _lastCleanup = DateTimeOffset.Now;

    private volatile bool _isProcessingSchedule;

    private Task? _loopTask;
    private CancellationTokenSource? _loopCts;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);

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
            await SaveToDiskAsync(cancellationToken);
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
            await SaveToDiskAsync(cancellationToken);
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
                CleanupNotificationCache();
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

                if (schedule.Type == ScheduleType.StopRecurring)
                {
                    if (timeLeft <= TimeSpan.FromSeconds(15) && timeLeft > TimeSpan.Zero)
                    {
                        await CheckAndNotifyStopAsync(schedule, runTime, TimeSpan.FromSeconds(15), "15 seconds", ct);
                    }
                    else if (timeLeft <= TimeSpan.FromMinutes(1) && timeLeft > TimeSpan.Zero)
                    {
                        await CheckAndNotifyStopAsync(schedule, runTime, TimeSpan.FromMinutes(1), "1 minute", ct);
                    }
                    else if (timeLeft <= TimeSpan.FromMinutes(5) && timeLeft > TimeSpan.Zero)
                    {
                        await CheckAndNotifyStopAsync(schedule, runTime, TimeSpan.FromMinutes(5), "5 minutes", ct);
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
                }
                else if (schedule.Type == ScheduleType.StopDuration)
                {
                    // StopDuration schedules are handled by ISessionAutoStopService when session starts
                    // They don't run on a fixed time, but track duration from session start
                }
                else
                {
                    if (sessionManager.IsSessionRunning)
                    {
                        continue;
                    }

                    if (timeLeft <= TimeSpan.FromSeconds(15) && timeLeft > TimeSpan.Zero)
                    {
                        await CheckAndNotifyAsync(schedule, runTime, TimeSpan.FromSeconds(15), "15 seconds", ct);
                    }
                    else if (timeLeft <= TimeSpan.FromMinutes(1) && timeLeft > TimeSpan.Zero)
                    {
                        await CheckAndNotifyAsync(schedule, runTime, TimeSpan.FromMinutes(1), "1 minute", ct);
                    }
                    else if (timeLeft <= TimeSpan.FromMinutes(5) && timeLeft > TimeSpan.Zero)
                    {
                        await CheckAndNotifyAsync(schedule, runTime, TimeSpan.FromMinutes(5), "5 minutes", ct);
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
                }
            }
        }
        finally
        {
            _lock.Release();
        }

        toStop = toStop.OrderBy(x => x.RunTime).ToList();
        toRun = toRun.OrderBy(x => x.RunTime).ToList();

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

    private async Task CheckAndNotifyStopAsync(SessionSchedule schedule, DateTimeOffset runTime, TimeSpan threshold,
        string timeText, CancellationToken ct)
    {
        var key = $"stop_{schedule.Id}_{runTime.Ticks}_{threshold.TotalSeconds}";

        if (!_sentNotificationKeys.Add(key))
        {
            return;
        }

        logger.LogInformation("Sending stop schedule warning: {Name} in {TimeText}", schedule.Name, timeText);

        await notifier.ShowSystemAsync("Session Scheduler", $"Session will stop in {timeText}.");
    }

    private async Task CheckAndNotifyAsync(SessionSchedule schedule, DateTimeOffset runTime, TimeSpan threshold,
        string timeText, CancellationToken ct)
    {
        var key = $"{schedule.Id}_{runTime.Ticks}_{threshold.TotalSeconds}";

        if (!_sentNotificationKeys.Add(key))
        {
            return;
        }

        var preset = await presetManager.GetPresetByIdAsync(schedule.PresetId, ct);
        if (preset == null)
        {
            return;
        }

        logger.LogInformation("Sending schedule warning: {Name} in {TimeText}", schedule.Name, timeText);

        await notifier.ShowSystemAsync("Session Scheduler", $"Session '{preset.Name}' will start in {timeText}.");
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
                logger.LogWarning("Schedule file {Path} exceeds maximum size limit ({Size} bytes)", TelemetryGuard.SafePath(_storagePath),
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