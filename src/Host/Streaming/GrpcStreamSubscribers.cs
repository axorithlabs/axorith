using System.Collections.Concurrent;
using System.Threading.Channels;
using Grpc.Core;

namespace Axorith.Host.Streaming;

internal sealed class GrpcStreamSubscribers<T>(ILogger logger, int capacity, string streamName) : IDisposable
{
    private sealed class Subscriber(Channel<T> queue, CancellationTokenSource cts)
    {
        public Channel<T> Queue { get; } = queue;
        public CancellationTokenSource Cts { get; } = cts;
    }

    private readonly ConcurrentDictionary<string, Subscriber> _items = new();

    public bool IsEmpty => _items.IsEmpty;

    public async Task SubscribeAsync(string key, IServerStreamWriter<T> stream, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(stream);

        var queue = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var subscriber = new Subscriber(queue, cts);

        _items.AddOrUpdate(key, subscriber, (_, old) =>
        {
            Stop(old);
            return subscriber;
        });

        _ = PumpAsync(key, stream, subscriber);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal disconnect.
        }
        finally
        {
            _items.TryRemove(new KeyValuePair<string, Subscriber>(key, subscriber));
            Stop(subscriber);
        }
    }

    public void Publish(T value, Func<string, bool>? filter = null)
    {
        foreach (var (key, subscriber) in _items)
        {
            if (filter == null || filter(key))
            {
                subscriber.Queue.Writer.TryWrite(value);
            }
        }
    }

    private async Task PumpAsync(string key, IServerStreamWriter<T> stream, Subscriber subscriber)
    {
        try
        {
            await foreach (var value in subscriber.Queue.Reader.ReadAllAsync(subscriber.Cts.Token).ConfigureAwait(false))
            {
                await stream.WriteAsync(value, subscriber.Cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal disconnect.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send {StreamName} to subscriber {SubscriberId}, removing",
                streamName, key);
            try
            {
                await subscriber.Cts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Replaced concurrently.
            }
        }
        finally
        {
            subscriber.Queue.Writer.TryComplete();
        }
    }

    private static void Stop(Subscriber subscriber)
    {
        try
        {
            subscriber.Cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        subscriber.Queue.Writer.TryComplete();
        subscriber.Cts.Dispose();
    }

    public void Dispose()
    {
        foreach (var subscriber in _items.Values)
        {
            Stop(subscriber);
        }

        _items.Clear();
    }
}
