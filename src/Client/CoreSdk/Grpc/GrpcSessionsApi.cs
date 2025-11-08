using System.Reactive.Linq;
using System.Reactive.Subjects;
using Axorith.Contracts;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Polly.Retry;

namespace Axorith.Client.CoreSdk.Grpc;

/// <summary>
///     gRPC implementation of ISessionsApi with streaming event support.
/// </summary>
internal class GrpcSessionsApi : ISessionsApi, IDisposable
{
    private readonly SessionsService.SessionsServiceClient _client;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly ILogger _logger;
    private readonly Subject<SessionEvent> _eventsSubject;
    private readonly CancellationTokenSource _streamCts;
    private readonly Task? _streamTask;
    private bool _disposed;

    public GrpcSessionsApi(SessionsService.SessionsServiceClient client, AsyncRetryPolicy retryPolicy,
        ILogger logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _eventsSubject = new Subject<SessionEvent>();
        _streamCts = new CancellationTokenSource();

        // Start streaming events immediately
        _streamTask = StartStreamingEventsAsync(_streamCts.Token);
    }

    public IObservable<SessionEvent> SessionEvents => _eventsSubject.AsObservable();

    public async Task<SessionState?> GetCurrentSessionAsync(CancellationToken ct = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var response = await _client.GetSessionStateAsync(
                    new GetSessionStateRequest(),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            if (!response.IsActive)
                return null;

            Guid? presetId = null;
            if (Guid.TryParse(response.PresetId, out var parsedId)) presetId = parsedId;

            DateTimeOffset? startedAt = null;
            if (response.StartedAt != null) startedAt = response.StartedAt.ToDateTimeOffset();

            return new SessionState(
                response.IsActive,
                presetId,
                response.PresetName,
                startedAt);
        }).ConfigureAwait(false);
    }

    public async Task<OperationResult> StartSessionAsync(Guid presetId, CancellationToken ct = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var response = await _client.StartSessionAsync(
                    new StartSessionRequest { PresetId = presetId.ToString() },
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return new OperationResult(
                response.Success,
                response.Message,
                response.Errors?.Count > 0 ? response.Errors.ToList() : null,
                response.Warnings?.Count > 0 ? response.Warnings.ToList() : null);
        }).ConfigureAwait(false);
    }

    public async Task<OperationResult> StopSessionAsync(CancellationToken ct = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var response = await _client.StopSessionAsync(
                    new StopSessionRequest(),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return new OperationResult(
                response.Success,
                response.Message,
                response.Errors?.Count > 0 ? response.Errors.ToList() : null,
                response.Warnings?.Count > 0 ? response.Warnings.ToList() : null);
        }).ConfigureAwait(false);
    }

    private async Task StartStreamingEventsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
            try
            {
                _logger.LogInformation("Starting session events stream...");

                using var call = _client.StreamSessionEvents(new StreamSessionEventsRequest(),
                    cancellationToken: ct);

                await foreach (var evt in call.ResponseStream.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    // Convert protobuf event to CoreSdk event
                    Guid? presetId = null;
                    if (!string.IsNullOrWhiteSpace(evt.PresetId) && Guid.TryParse(evt.PresetId, out var parsed))
                        presetId = parsed;

                    var sessionEvent = new SessionEvent(
                        (SessionEventType)evt.Type,
                        presetId,
                        evt.Message,
                        evt.Timestamp.ToDateTimeOffset());

                    _eventsSubject.OnNext(sessionEvent);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                _logger.LogInformation("Session events stream cancelled");
                break;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Session events stream cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Session events stream error, reconnecting in 5s...");

                try
                {
                    await Task.Delay(5000, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _streamCts.Cancel();
        _streamCts.Dispose();

        _eventsSubject.OnCompleted();
        _eventsSubject.Dispose();

        // Wait for stream task to complete (with timeout)
        if (_streamTask != null)
            try
            {
                _streamTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Ignore timeout
            }

        GC.SuppressFinalize(this);
    }
}