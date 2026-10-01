using Axorith.Sdk.Services;

namespace Axorith.Client.Services.Abstractions;

public interface IToastNotificationService
{
    void Show(string message, NotificationType type = NotificationType.Info, string? category = null);
    IObservable<ToastNotification> Notifications { get; }
}

public record ToastNotification(string Message, NotificationType Type, Guid Id, string? Category = null);
