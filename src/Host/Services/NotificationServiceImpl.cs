using Axorith.Contracts;
using Axorith.Host.Streaming;
using Grpc.Core;

namespace Axorith.Host.Services;

public class NotificationServiceImpl(
    NotificationBroadcaster broadcaster,
    ILogger<NotificationServiceImpl> logger) : NotificationService.NotificationServiceBase
{
    public override async Task StreamNotifications(StreamNotificationsRequest request,
        IServerStreamWriter<NotificationEvent> responseStream, ServerCallContext context)
    {
        var subscriberId = Guid.NewGuid().ToString();

        try
        {
            logger.LogInformation("Client {SubscriberId} started streaming notifications", subscriberId);

            await broadcaster.SubscribeAsync(subscriberId, responseStream, context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Client {SubscriberId} notification stream cancelled", subscriberId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error streaming notifications for {SubscriberId}", subscriberId);
            throw;
        }
    }
}