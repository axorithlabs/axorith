using Axorith.Host.Streaming;
using Axorith.Sdk.Services;
using Axorith.Shared.Platform;

namespace Axorith.Host.Services;

/// <summary>
///     Implementation of INotifier for the Host process.
///     Routes system notifications to the OS and toast notifications to connected gRPC clients.
/// </summary>
public class HostNotifier(
    ISystemNotificationService systemNotificationService,
    NotificationBroadcaster notificationBroadcaster,
    ILogger<HostNotifier> logger) : INotifier
{
    public void ShowToast(string message, NotificationType type = NotificationType.Info)
    {
        // Fire and forget broadcast to clients
        _ = notificationBroadcaster.BroadcastAsync(message, type);
    }

    public async Task ShowSystemAsync(string title, string message, TimeSpan? expiration = null)
    {
        try
        {
            if (notificationBroadcaster.HasSubscribers)
            {
                var combinedMessage = string.IsNullOrWhiteSpace(title)
                    ? message
                    : $"{title}: {message}";

                await notificationBroadcaster.BroadcastAsync(combinedMessage, NotificationType.Info, "System");
            }
            else
            {
                await systemNotificationService.ShowNotificationAsync(title, message, expiration);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to show system notification: {Title}", title);
        }
    }
}