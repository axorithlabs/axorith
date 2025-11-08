using System.Collections.Concurrent;
using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Mappers;
using Grpc.Core;

namespace Axorith.Host.Streaming;

/// <summary>
///     Broadcasts session events from Core ISessionManager to all connected gRPC clients.
///     Thread-safe implementation using ConcurrentDictionary.
/// </summary>
public class SessionEventBroadcaster : IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<SessionEventBroadcaster> _logger;
    private readonly ConcurrentDictionary<string, IServerStreamWriter<SessionEvent>> _subscribers = new();
    private bool _disposed;

    public SessionEventBroadcaster(ISessionManager sessionManager, ILogger<SessionEventBroadcaster> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to SessionManager events
        _sessionManager.SessionStarted += OnSessionStarted;
        _sessionManager.SessionStopped += OnSessionStopped;

        _logger.LogInformation("SessionEventBroadcaster initialized");
    }

    /// <summary>
    ///     Subscribes a gRPC client to session events.
    ///     Blocks until cancellation or error.
    /// </summary>
    public async Task SubscribeAsync(string subscriberId, IServerStreamWriter<SessionEvent> stream,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriberId);
        ArgumentNullException.ThrowIfNull(stream);

        _logger.LogInformation("Client {SubscriberId} subscribed to session events", subscriberId);

        if (!_subscribers.TryAdd(subscriberId, stream))
        {
            _logger.LogWarning("Client {SubscriberId} already subscribed, replacing stream", subscriberId);
            _subscribers[subscriberId] = stream;
        }

        try
        {
            // Keep connection alive until cancellation
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Client {SubscriberId} unsubscribed (cancelled)", subscriberId);
        }
        finally
        {
            _subscribers.TryRemove(subscriberId, out _);
        }
    }

    private void OnSessionStarted(Guid presetId)
    {
        var evt = SessionMapper.CreateEvent(SessionEventType.SessionEventStarted, presetId, "Session started");
        _ = BroadcastAsync(evt);
    }

    private void OnSessionStopped(Guid presetId)
    {
        var evt = SessionMapper.CreateEvent(SessionEventType.SessionEventStopped, presetId, "Session stopped");
        _ = BroadcastAsync(evt);
    }

    /// <summary>
    ///     Broadcasts event to all subscribers in parallel.
    ///     Automatically removes dead subscribers on write failure.
    /// </summary>
    private async Task BroadcastAsync(SessionEvent evt)
    {
        if (_disposed || _subscribers.IsEmpty)
            return;

        _logger.LogDebug("Broadcasting {EventType} to {Count} subscribers",
            evt.Type, _subscribers.Count);

        // Create snapshot to avoid race condition during iteration
        var subscribersSnapshot = _subscribers.ToArray();

        // Use Parallel.ForEachAsync to reduce Task allocations
        await Parallel.ForEachAsync(subscribersSnapshot, async (kvp, ct) =>
        {
            var (subscriberId, stream) = kvp;
            try
            {
                await stream.WriteAsync(evt, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write to subscriber {SubscriberId}, removing", subscriberId);
                _subscribers.TryRemove(subscriberId, out _);
            }
        }).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _sessionManager.SessionStarted -= OnSessionStarted;
        _sessionManager.SessionStopped -= OnSessionStopped;

        _subscribers.Clear();

        _logger.LogInformation("SessionEventBroadcaster disposed");

        GC.SuppressFinalize(this);
    }
}