using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Contracts;
using Polly.Retry;
using HealthStatus = Axorith.Client.CoreSdk.Abstractions.HealthStatus;

namespace Axorith.Client.CoreSdk;

/// <summary>
///     gRPC implementation of IDiagnosticsApi.
/// </summary>
internal class GrpcDiagnosticsApi(
    DiagnosticsService.DiagnosticsServiceClient client,
    AsyncRetryPolicy retryPolicy)
    : IDiagnosticsApi
{
    public async Task<HealthStatus> GetHealthAsync(CancellationToken ct = default)
    {
        return await retryPolicy.ExecuteAsync(async () =>
        {
            var response = await client.GetHealthAsync(
                    new HealthCheckRequest(),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return new HealthStatus(
                (HealthState)response.Status,
                response.Version,
                response.UptimeStarted.ToDateTimeOffset(),
                response.ActiveSessions,
                response.LoadedModules);
        }).ConfigureAwait(false);
    }
}