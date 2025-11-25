using System.Text.Json;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Axorith.Core.Services;

public class ScheduleManager(
    string storageDirectory,
    ISessionManager sessionManager,
    IPresetManager presetManager,
    ILogger<ScheduleManager> logger)
    : IScheduleManager
{
    private readonly string _storagePath = Path.Combine(storageDirectory, "schedules.json");

    private readonly List<SessionSchedule> _schedules = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private Task? _loopTask;
    private CancellationTokenSource? _loopCts;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunSchedulerLoopAsync(_loopCts.Token);

        logger.LogInformation("Scheduler started with {Count} schedules", _schedules.Count);
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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await CheckAndRunSchedulesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in scheduler loop");
            }
        }
    }

    private async Task CheckAndRunSchedulesAsync(CancellationToken ct)
    {
        if (sessionManager.IsSessionRunning)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        List<SessionSchedule> toRun = [];
        
        logger.LogDebug("Checking schedules at {Now}. Active schedules: {Count}", now, _schedules.Count(s => s.IsEnabled));

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

                if (!nextRun.HasValue || nextRun.Value > now.AddSeconds(5) || nextRun.Value < now.AddMinutes(-5))
                {
                    continue;
                }

                if (schedule.LastRun.HasValue && (now - schedule.LastRun.Value).TotalMinutes < 2)
                {
                    continue;
                }

                toRun.Add(schedule);
            }
        }
        finally
        {
            _lock.Release();
        }

        foreach (var schedule in toRun)
        {
            logger.LogInformation("Triggering schedule '{Name}' for preset {PresetId}", schedule.Name,
                schedule.PresetId);

            var preset = await presetManager.GetPresetByIdAsync(schedule.PresetId, ct);
            if (preset == null)
            {
                logger.LogWarning("Preset {PresetId} not found for schedule '{Name}'. Disabling schedule.",
                    schedule.PresetId, schedule.Name);
                await SetEnabledAsync(schedule.Id, false, ct);
                continue;
            }

            try
            {
                await sessionManager.StartSessionAsync(preset, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to auto-start session...");
    
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
            await using var stream = File.OpenRead(_storagePath);
            var loaded = await JsonSerializer.DeserializeAsync<List<SessionSchedule>>(stream, _jsonOptions, ct);
            if (loaded != null)
            {
                _schedules.Clear();
                _schedules.AddRange(loaded);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load schedules from {Path}", _storagePath);
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
            logger.LogError(ex, "Failed to save schedules to {Path}", _storagePath);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _loopCts?.Cancel();
        if (_loopTask != null)
        {
            try
            {
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