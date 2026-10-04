using System.Reactive.Linq;
using System.Reactive.Subjects;
using Axorith.Core.Models;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Contracts;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Polly.Retry;
using OperationResult = Axorith.Client.CoreSdk.Abstractions.OperationResult;
using SessionEvent = Axorith.Client.CoreSdk.Abstractions.SessionEvent;
using SessionEventType = Axorith.Client.CoreSdk.Abstractions.SessionEventType;
using SessionState = Axorith.Client.CoreSdk.Abstractions.SessionState;
using FocusCommitmentMode = Axorith.Core.Models.FocusCommitmentMode;
using AfterEndBehavior = Axorith.Core.Models.AfterEndBehavior;

namespace Axorith.Client.CoreSdk;

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
        _client = client;
        _retryPolicy = retryPolicy;
        _logger = logger;

        _eventsSubject = new Subject<SessionEvent>();
        _streamCts = new CancellationTokenSource();

        _streamTask = GrpcStreamRunner.RunAsync(async token =>
        {
            _logger.LogInformation("Starting session events stream...");
            using var call = _client.StreamSessionEvents(new StreamSessionEventsRequest(), cancellationToken: token);
            await foreach (var evt in call.ResponseStream.ReadAllAsync(token).ConfigureAwait(false))
            {
                var presetId = Guid.TryParse(evt.PresetId, out var parsed) ? parsed : (Guid?)null;
                _eventsSubject.OnNext(new SessionEvent(
                    (SessionEventType)evt.Type,
                    presetId,
                    evt.Message,
                    evt.Timestamp.ToDateTimeOffset()));
            }
        }, _logger, "Session events", _streamCts.Token);
    }

    public IObservable<SessionEvent> SessionEvents => _eventsSubject.AsObservable();

    public async Task<IReadOnlyList<SessionActivity>> GetSessionHistoryAsync(CancellationToken ct = default) =>
        await _retryPolicy.ExecuteAsync(async () =>
        {
            var response = await _client.GetSessionHistoryAsync(new GetSessionStateRequest(), cancellationToken: ct)
                .ConfigureAwait(false);
            return response.Entries.Select(entry => new SessionActivity(
                DateTimeOffset.FromUnixTimeMilliseconds(entry.StartedAtUnixMs),
                DateTimeOffset.FromUnixTimeMilliseconds(entry.EndedAtUnixMs), entry.PresetName)).ToArray();
        }).ConfigureAwait(false);

    public Task<SessionState?> GetCurrentSessionAsync(CancellationToken ct = default)
    {
        return _retryPolicy.ExecuteAsync(async () =>
        {
            var response = await _client.GetSessionStateAsync(
                    new GetSessionStateRequest(),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            if (!response.IsActive)
            {
                return null;
            }

            Guid? presetId = null;
            if (Guid.TryParse(response.PresetId, out var parsedId))
            {
                presetId = parsedId;
            }

            DateTimeOffset? startedAt = null;
            if (response.StartedAt != null)
            {
                startedAt = response.StartedAt.ToDateTimeOffset();
            }

            DateTimeOffset? endsAt = null;
            if (response.EndsAt != null)
            {
                endsAt = response.EndsAt.ToDateTimeOffset();
            }

            DateTimeOffset? breakEndsAt = null;
            if (response.BreakEndsAt != null)
            {
                breakEndsAt = response.BreakEndsAt.ToDateTimeOffset();
            }

            return new SessionState(
                response.IsActive,
                presetId,
                response.PresetName,
                startedAt,
                (FocusCommitmentMode)response.FocusCommitment,
                endsAt,
                response.BreaksRemaining,
                (AfterEndBehavior)response.AfterEnd,
                response.ProtectionStatus,
                response.EmergencyUnlockAvailable,
                breakEndsAt,
                response.AppBlocking,
                response.WebsiteBlocking,
                response.BreaksTotal,
                TimeSpan.FromSeconds(response.RemainingSeconds),
                response.BreakEndsAt == null ? null : TimeSpan.FromSeconds(response.BreakRemainingSeconds));
        });
    }

    public Task<OperationResult> StartSessionAsync(Guid presetId, Guid sessionInstanceId, CancellationToken ct = default)
    {
        return _retryPolicy.ExecuteAsync(async () =>
        {
            var response = await _client.StartSessionAsync(
                    new StartSessionRequest
                    {
                        PresetId = presetId.ToString(),
                        SessionInstanceId = sessionInstanceId.ToString(),
                        StartSource = "manual"
                    },
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return GrpcResultMapper.ToModel(response);
        });
    }

    public Task<OperationResult> StopSessionAsync(CancellationToken ct = default)
    {
        return _retryPolicy.ExecuteAsync(async () =>
        {
            var response = await _client.StopSessionAsync(
                    new StopSessionRequest(),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return GrpcResultMapper.ToModel(response);
        });
    }

    public async Task<OperationResult> StartBreakAsync(CancellationToken ct = default)
    {
        var response = await _client.StartBreakAsync(new StartBreakRequest(), cancellationToken: ct)
            .ConfigureAwait(false);
        return GrpcResultMapper.ToModel(response);
    }

    public Task<OperationResult> PreflightSessionAsync(Guid presetId, CancellationToken ct = default)
    {
        return _retryPolicy.ExecuteAsync(async () =>
        {
            var response = await _client.PreflightSessionAsync(
                    new PreflightSessionRequest { PresetId = presetId.ToString() },
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return GrpcResultMapper.ToModel(response);
        });
    }

    public async IAsyncEnumerable<EmergencyUnlockProgress> HoldEmergencyUnlockAsync(
        IAsyncEnumerable<bool> heldSignals,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var call = _client.HoldEmergencyUnlock(cancellationToken: ct);
        using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sendTask = Task.Run(async () =>
        {
            await foreach (var isHeld in heldSignals.WithCancellation(sendCts.Token).ConfigureAwait(false))
            {
                await call.RequestStream.WriteAsync(new EmergencyUnlockHoldSignal { IsHeld = isHeld })
                    .ConfigureAwait(false);
            }

            await call.RequestStream.CompleteAsync().ConfigureAwait(false);
        }, sendCts.Token);

        try
        {
            while (await call.ResponseStream.MoveNext(ct).ConfigureAwait(false))
            {
                var progress = call.ResponseStream.Current;
                yield return new EmergencyUnlockProgress(progress.Progress, progress.Completed, progress.Message);
                if (progress.Completed)
                {
                    break;
                }
            }
        }
        finally
        {
            sendCts.Cancel();
            try
            {
                await sendTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (sendCts.IsCancellationRequested)
            {
            }
            catch (RpcException ex) when (sendCts.IsCancellationRequested &&
                                          ex.StatusCode is StatusCode.OK or StatusCode.Cancelled)
            {
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _streamCts.Cancel();
        _streamCts.Dispose();

        _eventsSubject.OnCompleted();
        _eventsSubject.Dispose();

        _streamTask?.Wait(TimeSpan.FromSeconds(5));

        GC.SuppressFinalize(this);
    }
}
