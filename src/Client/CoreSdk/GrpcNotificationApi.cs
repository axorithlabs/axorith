using System.Runtime.CompilerServices;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Contracts;
using Grpc.Core;
using NotificationEvent = Axorith.Client.CoreSdk.Abstractions.NotificationEvent;
using NotificationType = Axorith.Sdk.Services.NotificationType;

namespace Axorith.Client.CoreSdk;

internal class GrpcNotificationApi(
    NotificationService.NotificationServiceClient client) : INotificationApi
{
    public async IAsyncEnumerable<NotificationEvent> StreamNotificationsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var call = client.StreamNotifications(new StreamNotificationsRequest(), cancellationToken: ct);

        await foreach (var evt in call.ResponseStream.ReadAllAsync(ct))
        {
            yield return new NotificationEvent(
                evt.Message,
                MapType(evt.Type),
                evt.Timestamp.ToDateTimeOffset(),
                evt.Source
            );
        }
    }

    private static NotificationType MapType(Contracts.NotificationType type)
    {
        return type switch
        {
            Contracts.NotificationType.Info => NotificationType.Info,
            Contracts.NotificationType.Success => NotificationType.Success,
            Contracts.NotificationType.Warning => NotificationType.Warning,
            Contracts.NotificationType.Error => NotificationType.Error,
            _ => NotificationType.Info
        };
    }
}