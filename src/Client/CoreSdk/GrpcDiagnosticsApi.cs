using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Contracts;
using Polly.Retry;
using HealthStatus = Axorith.Client.CoreSdk.Abstractions.HealthStatus;

namespace Axorith.Client.CoreSdk;

internal class GrpcDiagnosticsApi(
    DiagnosticsService.DiagnosticsServiceClient client,
    AsyncRetryPolicy retryPolicy)
    : IDiagnosticsApi
{
    public Task<HealthStatus> GetHealthAsync(CancellationToken ct = default) => GrpcResultMapper.ExecuteAsync(retryPolicy,
        () => client.GetHealthAsync(new HealthCheckRequest(), cancellationToken: ct), response => new HealthStatus(
            (HealthState)response.Status,
            response.Version,
            response.UptimeStarted.ToDateTimeOffset(),
            response.ActiveSessions,
            response.LoadedModules));
}
