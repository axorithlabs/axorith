using Axorith.Contracts;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using NotificationType = Axorith.Sdk.Services.NotificationType;

namespace Axorith.Host.Streaming;

public sealed class NotificationBroadcaster(ILogger<NotificationBroadcaster> logger) : IDisposable
{
    private readonly GrpcStreamSubscribers<NotificationEvent> _subscribers = new(logger, 100, "notification");

    public bool HasSubscribers => !_subscribers.IsEmpty;

    public async Task SubscribeAsync(string subscriberId, IServerStreamWriter<NotificationEvent> stream,
        CancellationToken ct)
    {
        logger.LogInformation("Client {SubscriberId} subscribed to notifications", subscriberId);
        await _subscribers.SubscribeAsync(subscriberId, stream, ct).ConfigureAwait(false);
        logger.LogInformation("Client {SubscriberId} unsubscribed from notifications", subscriberId);
    }

    public Task BroadcastAsync(string message, NotificationType type, string source = "Axorith")
    {
        if (_subscribers.IsEmpty)
        {
            return Task.CompletedTask;
        }

        var evt = new NotificationEvent
        {
            Message = message,
            Type = type switch
            {
                NotificationType.Info => Contracts.NotificationType.Info,
                NotificationType.Success => Contracts.NotificationType.Success,
                NotificationType.Warning => Contracts.NotificationType.Warning,
                NotificationType.Error => Contracts.NotificationType.Error,
                _ => Contracts.NotificationType.Info
            },
            Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            Source = source
        };

        _subscribers.Publish(evt);
        return Task.CompletedTask;
    }

    public void Dispose() => _subscribers.Dispose();
}
