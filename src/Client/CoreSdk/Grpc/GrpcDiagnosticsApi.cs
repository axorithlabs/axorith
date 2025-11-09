using Axorith.Contracts;
using Polly.Retry;

namespace Axorith.Client.CoreSdk.Grpc;

/// <summary>
///     gRPC implementation of IDiagnosticsApi.
/// </summary>
internal class GrpcDiagnosticsApi(
    DiagnosticsService.DiagnosticsServiceClient client,
    AsyncRetryPolicy retryPolicy)
    : IDiagnosticsApi
{
    private readonly DiagnosticsService.DiagnosticsServiceClient _client =
        client ?? throw new ArgumentNullException(nameof(client));

    private readonly AsyncRetryPolicy
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));

    public async Task<HealthStatus> GetHealthAsync(CancellationToken ct = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var response = await _client.GetHealthAsync(
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