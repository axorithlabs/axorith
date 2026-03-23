using System.Collections.Concurrent;
using System.Threading.Channels;
using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Mappers;
using Grpc.Core;

namespace Axorith.Host.Streaming;

/// <summary>
///     Broadcasts session events from Core ISessionManager to all connected gRPC clients.
///     Uses per-subscriber Channel queues to avoid ThreadPool Starvation from Parallel.ForEachAsync.
/// </summary>
public class SessionEventBroadcaster : IDisposable
{
    private sealed class Subscriber
    {
        public required IServerStreamWriter<SessionEvent> Stream { get; init; }
        public required Channel<SessionEvent> Queue { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public required Task Loop { get; init; }
    }

    private readonly ISessionManager _sessionManager;
    private readonly ILogger<SessionEventBroadcaster> _logger;
    private readonly ConcurrentDictionary<string, Subscriber> _subscribers = new();
    private bool _disposed;

    public SessionEventBroadcaster(ISessionManager sessionManager, ILogger<SessionEventBroadcaster> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;

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

        var channel = Channel.CreateBounded<SessionEvent>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var loopTask = Task.Run(async () =>
        {
            try
            {
                while (await channel.Reader.WaitToReadAsync(linkedCts.Token).ConfigureAwait(false))
                {
                    while (channel.Reader.TryRead(out var evt))
                    {
                        try
                        {
                            await stream.WriteAsync(evt, linkedCts.Token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "Failed to write to subscriber {SubscriberId}, removing", subscriberId);
                            try
                            {
                                await linkedCts.CancelAsync().ConfigureAwait(false);
                            }
                            catch (ObjectDisposedException)
                            {
                                // CTS was disposed by replacement subscriber
                            }

                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, linkedCts.Token);

        var subscriber = new Subscriber
        {
            Stream = stream,
            Queue = channel,
            Cts = linkedCts,
            Loop = loopTask
        };

        _subscribers.AddOrUpdate(subscriberId,
            _ => subscriber,
            (_, oldSubscriber) =>
            {
                try
                {
                    oldSubscriber.Cts.Cancel();
                    oldSubscriber.Queue.Writer.TryComplete();
                    oldSubscriber.Cts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed
                }

                return subscriber;
            });

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Client {SubscriberId} unsubscribed from session events", subscriberId);
        }
        finally
        {
            _subscribers.TryRemove(new KeyValuePair<string, Subscriber>(subscriberId, subscriber));

            try
            {
                await subscriber.Cts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Already disposed
            }

            subscriber.Queue.Writer.TryComplete();
            subscriber.Cts.Dispose();
        }
    }

    private void OnSessionStarted(Guid presetId)
    {
        var evt = SessionMapper.CreateEvent(SessionEventType.SessionEventStarted, presetId, "Session started");
        Broadcast(evt);
    }

    private void OnSessionStopped(Guid presetId)
    {
        var evt = SessionMapper.CreateEvent(SessionEventType.SessionEventStopped, presetId, "Session stopped");
        Broadcast(evt);
    }

    /// <summary>
    ///     Non-blocking broadcast to all subscribers via their Channel queues.
    /// </summary>
    private void Broadcast(SessionEvent evt)
    {
        if (_disposed || _subscribers.IsEmpty)
        {
            return;
        }

        _logger.LogDebug("Broadcasting {EventType} to {Count} subscribers",
            evt.Type, _subscribers.Count);

        foreach (var sub in _subscribers.Values)
        {
            sub.Queue.Writer.TryWrite(evt);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _sessionManager.SessionStarted -= OnSessionStarted;
        _sessionManager.SessionStopped -= OnSessionStopped;

        foreach (var sub in _subscribers.Values)
        {
            try
            {
                sub.Cts.Cancel();
                sub.Queue.Writer.TryComplete();
                sub.Cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed
            }
        }

        _subscribers.Clear();

        _logger.LogInformation("SessionEventBroadcaster disposed");

        GC.SuppressFinalize(this);
    }
}
