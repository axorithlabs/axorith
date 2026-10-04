using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Mappers;
using Grpc.Core;

namespace Axorith.Host.Streaming;

public sealed class SessionEventBroadcaster : IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<SessionEventBroadcaster> _logger;
    private readonly GrpcStreamSubscribers<SessionEvent> _subscribers;
    private bool _disposed;

    public SessionEventBroadcaster(ISessionManager sessionManager, ILogger<SessionEventBroadcaster> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
        _subscribers = new GrpcStreamSubscribers<SessionEvent>(logger, 100, "session event");

        _sessionManager.SessionStarted += OnSessionStarted;
        _sessionManager.SessionStopped += OnSessionStopped;
        _logger.LogInformation("SessionEventBroadcaster initialized");
    }

    public async Task SubscribeAsync(string subscriberId, IServerStreamWriter<SessionEvent> stream,
        CancellationToken ct)
    {
        _logger.LogInformation("Client {SubscriberId} subscribed to session events", subscriberId);
        await _subscribers.SubscribeAsync(subscriberId, stream, ct).ConfigureAwait(false);
        _logger.LogInformation("Client {SubscriberId} unsubscribed from session events", subscriberId);
    }

    private void OnSessionStarted(Guid presetId) =>
        _subscribers.Publish(SessionMapper.CreateEvent(SessionEventType.SessionEventStarted, presetId, "Session started"));

    private void OnSessionStopped(Guid presetId) =>
        _subscribers.Publish(SessionMapper.CreateEvent(SessionEventType.SessionEventStopped, presetId, "Session stopped"));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sessionManager.SessionStarted -= OnSessionStarted;
        _sessionManager.SessionStopped -= OnSessionStopped;
        _subscribers.Dispose();
        _logger.LogInformation("SessionEventBroadcaster disposed");
        GC.SuppressFinalize(this);
    }
}
