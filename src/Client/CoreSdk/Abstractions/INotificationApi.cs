using Axorith.Sdk.Services;

namespace Axorith.Client.CoreSdk.Abstractions;

public interface INotificationApi
{
    IAsyncEnumerable<NotificationEvent> StreamNotificationsAsync(CancellationToken ct = default);
}

public record NotificationEvent(string Message, NotificationType Type, DateTimeOffset Timestamp, string Source);