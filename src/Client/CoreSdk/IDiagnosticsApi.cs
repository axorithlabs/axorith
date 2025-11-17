namespace Axorith.Client.CoreSdk;

/// <summary>
///     API for diagnostics and health checks.
/// </summary>
public interface IDiagnosticsApi
{
    /// <summary>
    ///     Gets health status of the Core services.
    /// </summary>
    Task<HealthStatus> GetHealthAsync(CancellationToken ct = default);
}

/// <summary>
///     Health status information.
/// </summary>
public record HealthStatus(
    HealthState State,
    string Version,
    DateTimeOffset UptimeStarted,
    int ActiveSessions,
    int LoadedModules
);

/// <summary>
///     Health status states.
/// </summary>
public enum HealthState
{
    /// <summary>All systems operational.</summary>
    Healthy,

    /// <summary>Partial functionality available.</summary>
    Degraded,

    /// <summary>System is not functioning properly.</summary>
    Unhealthy
}