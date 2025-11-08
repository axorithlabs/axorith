using Axorith.Contracts;
using Microsoft.Extensions.Logging;
using Polly.Retry;

namespace Axorith.Client.CoreSdk.Grpc;

/// <summary>
///     gRPC implementation of IDiagnosticsApi.
/// </summary>
internal class GrpcDiagnosticsApi : IDiagnosticsApi
{
    private readonly DiagnosticsService.DiagnosticsServiceClient _client;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly ILogger _logger;

    public GrpcDiagnosticsApi(DiagnosticsService.DiagnosticsServiceClient client,
        AsyncRetryPolicy retryPolicy, ILogger logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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