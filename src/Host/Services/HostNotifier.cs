using Axorith.Host.Streaming;
using Axorith.Sdk.Services;
using Axorith.Shared.Platform;

namespace Axorith.Host.Services;

public class HostNotifier(
    ISystemNotificationService systemNotificationService,
    NotificationBroadcaster notificationBroadcaster,
    ILogger<HostNotifier> logger) : INotifier
{
    public void ShowToast(string message, NotificationType type = NotificationType.Info) => ShowToast(message, type, "Axorith");

    public void ShowToast(string message, NotificationType type, string? category)
    {
        // Fire and forget broadcast to clients
        _ = notificationBroadcaster.BroadcastAsync(message, type, GetCategory(category));
    }

    public Task ShowSystemAsync(string title, string message, TimeSpan? expiration = null) =>
        ShowSystemAsync(title, message, title, expiration);

    public async Task ShowSystemAsync(string title, string message, string? category, TimeSpan? expiration = null)
    {
        try
        {
            if (notificationBroadcaster.HasSubscribers)
            {
                var source = GetCategory(category, title);
                var combinedMessage = string.IsNullOrWhiteSpace(title) ||
                                      string.Equals(title, source, StringComparison.OrdinalIgnoreCase)
                    ? message
                    : $"{title}: {message}";

                await notificationBroadcaster.BroadcastAsync(combinedMessage, NotificationType.Info, source);
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

    private static string GetCategory(string? category, string? fallback = "Axorith") =>
        string.IsNullOrWhiteSpace(category)
            ? string.IsNullOrWhiteSpace(fallback) ? "Axorith" : fallback.Trim()
            : category.Trim();
}
