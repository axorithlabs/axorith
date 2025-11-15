using Axorith.Client.CoreSdk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Axorith.Client.Services;

/// <summary>
///     Monitors Host health and triggers reconnection if needed.
/// </summary>
public interface IHostHealthMonitor : IDisposable
{
    /// <summary>
    ///     Starts health monitoring in background.
    /// </summary>
    void Start();

    /// <summary>
    ///     Stops health monitoring.
    /// </summary>
    void Stop();

    /// <summary>
    ///     Checks if Host is healthy (single check).
    /// </summary>
    Task<bool> IsHostHealthyAsync();

    /// <summary>
    ///     Fires when Host becomes unhealthy.
    /// </summary>
    event Action? HostUnhealthy;

    /// <summary>
    ///     Fires when Host becomes healthy again.
    /// </summary>
    event Action? HostHealthy;
}

public class HostHealthMonitor(
    IDiagnosticsApi diagnosticsApi,
    IOptions<Configuration> config,
    ILogger<HostHealthMonitor> logger) : IHostHealthMonitor
{
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 500;

    private readonly CancellationTokenSource _monitoringCts = new();
    private Task? _monitoringTask;
    private bool _wasHealthy = true;

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
            return;

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

    public async Task<bool> IsHostHealthyAsync()
    {
        for (var i = 0; i < MaxRetries; i++)
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var healthStatus = await diagnosticsApi.GetHealthAsync(cts.Token);

                if (healthStatus.State == HealthState.Healthy) return true;

                logger.LogWarning("Host health check returned non-healthy status: {Status}", healthStatus.State);
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("Host health check timed out (attempt {Attempt}/{Max})", i + 1, MaxRetries);

                if (i < MaxRetries - 1) await Task.Delay(RetryDelayMs);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Health check failed (attempt {Attempt}/{Max})", i + 1, MaxRetries);

                if (i < MaxRetries - 1) await Task.Delay(RetryDelayMs);
            }

        return false;
    }

    private async Task MonitorHealthAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
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

    public void Dispose()
    {
        Stop();
        _monitoringCts.Dispose();
    }
}