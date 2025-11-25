using System.Collections.Concurrent;
using System.Threading.Channels;
using Axorith.Contracts;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using NotificationType = Axorith.Sdk.Services.NotificationType;

namespace Axorith.Host.Streaming;

/// <summary>
///     Broadcasts transient notifications (Toasts) to all connected gRPC clients.
/// </summary>
public class NotificationBroadcaster(ILogger<NotificationBroadcaster> logger)
{
    private sealed class Subscriber
    {
        public required IServerStreamWriter<NotificationEvent> Stream { get; init; }
        public required Channel<NotificationEvent> Queue { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public required Task Loop { get; init; }
    }

    private readonly ConcurrentDictionary<string, Subscriber> _subscribers = new();

    /// <summary>
    ///     Subscribes a gRPC client to the notification stream.
    /// </summary>
    public async Task SubscribeAsync(string subscriberId, IServerStreamWriter<NotificationEvent> stream,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriberId);
        ArgumentNullException.ThrowIfNull(stream);

        logger.LogInformation("Client {SubscriberId} subscribed to notifications", subscriberId);

        var channel = Channel.CreateBounded<NotificationEvent>(new BoundedChannelOptions(100)
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
                while (channel.Reader.TryRead(out var evt))
                    try
                    {
                        await stream.WriteAsync(evt, linkedCts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to send notification to subscriber {SubscriberId}, removing",
                            subscriberId);
                        await linkedCts.CancelAsync().ConfigureAwait(false);
                        return;
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
                }
                catch
                {
                    // ignore
                }

                return subscriber;
            });

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Client {SubscriberId} unsubscribed from notifications", subscriberId);
        }
        finally
        {
            if (_subscribers.TryRemove(new KeyValuePair<string, Subscriber>(subscriberId, subscriber)))
            {
                await subscriber.Cts.CancelAsync().ConfigureAwait(false);
                subscriber.Cts.Dispose();
            }
        }
    }

    /// <summary>
    ///     Broadcasts a notification to all connected clients.
    /// </summary>
    public Task BroadcastAsync(string message, NotificationType type, string source = "System")
    {
        if (_subscribers.IsEmpty)
        {
            return Task.CompletedTask;
        }

        var evt = new NotificationEvent
        {
            Message = message,
            Type = MapType(type),
            Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            Source = source
        };

        foreach (var sub in _subscribers.Values)
        {
            sub.Queue.Writer.TryWrite(evt);
        }

        return Task.CompletedTask;
    }

    private static Contracts.NotificationType MapType(NotificationType type)
    {
        return type switch
        {
            NotificationType.Info => Contracts.NotificationType.Info,
            NotificationType.Success => Contracts.NotificationType.Success,
            NotificationType.Warning => Contracts.NotificationType.Warning,
            NotificationType.Error => Contracts.NotificationType.Error,
            _ => Contracts.NotificationType.Info
        };
    }
}