namespace Axorith.Client.CoreSdk.Abstractions;

public interface IDiagnosticsApi
{
    Task<HealthStatus> GetHealthAsync(CancellationToken ct = default);
}

public record HealthStatus(
    HealthState State,
    string Version,
    DateTimeOffset UptimeStarted,
    int ActiveSessions,
    int LoadedModules
);

public enum HealthState
{
    Healthy,

    Degraded,

    Unhealthy
}
