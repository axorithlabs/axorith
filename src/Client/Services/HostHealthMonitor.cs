using Axorith.Client.CoreSdk.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Axorith.Client.Services;

public sealed class HostHealthMonitor(
    IOptions<Configuration> config,
    ILogger<HostHealthMonitor> logger)
{
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 500;

    private readonly CancellationTokenSource _monitoringCts = new();
    private Task? _monitoringTask;
    private bool _wasHealthy = true;
    private volatile bool _isPaused;
    private IDiagnosticsApi? _diagnosticsApi;

    public event Action? HostUnhealthy;
    public event Action? HostHealthy;

    public void Start()
    {
        if (_monitoringTask != null)
        {
            logger.LogWarning("Health monitoring already started");
            return;
        }

        logger.LogInformation("Starting host health monitoring (interval: {Interval}s)",
            config.Value.Host.HealthCheckInterval);
        _monitoringTask = MonitorHealthAsync(_monitoringCts.Token);
    }

    public void Stop()
    {
        if (_monitoringTask == null)
        {
            return;
        }

        logger.LogInformation("Stopping host health monitoring");
        _monitoringCts.Cancel();

        try
        {
            _monitoringTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
        {
            // Expected
        }
    }

    public void Pause()
    {
        _isPaused = true;
        logger.LogInformation("Health monitoring paused");
    }

    public void Resume()
    {
        _isPaused = false;
        logger.LogInformation("Health monitoring resumed");
    }

    public async Task<bool> IsHostHealthyAsync()
    {
        var diagnosticsApi = _diagnosticsApi;
        if (diagnosticsApi is null)
        {
            return false;
        }

        for (var i = 0; i < MaxRetries; i++)
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var healthStatus = await diagnosticsApi.GetHealthAsync(cts.Token);

                if (healthStatus.State == HealthState.Healthy)
                {
                    return true;
                }

                logger.LogWarning("Host health check returned non-healthy status: {Status}", healthStatus.State);
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("Host health check timed out (attempt {Attempt}/{Max})", i + 1, MaxRetries);

                if (i < MaxRetries - 1)
                {
                    await Task.Delay(RetryDelayMs);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Health check failed (attempt {Attempt}/{Max})", i + 1, MaxRetries);

                if (i < MaxRetries - 1)
                {
                    await Task.Delay(RetryDelayMs);
                }
            }

        return false;
    }

    private async Task MonitorHealthAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_isPaused)
                {
                    await Task.Delay(1000, ct);
                    continue;
                }

                var isHealthy = await IsHostHealthyAsync();

                switch (isHealthy)
                {
                    case true when !_wasHealthy:
                        logger.LogInformation("Host became healthy");
                        HostHealthy?.Invoke();
                        break;
                    case false when _wasHealthy:
                        logger.LogWarning("Host became unhealthy");
                        HostUnhealthy?.Invoke();
                        break;
                }

                _wasHealthy = isHealthy;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during health monitoring");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(config.Value.Host.HealthCheckInterval), ct);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
        }

        logger.LogInformation("Health monitoring stopped");
    }

    public void SetDiagnosticsApi(IDiagnosticsApi api) => _diagnosticsApi = api;

    public void Dispose()
    {
        Stop();
        _monitoringCts.Dispose();
    }
}
