using Axorith.Sdk.Services;

namespace Axorith.Client.Services.Abstractions;

public interface IToastNotificationService
{
    void Show(string message, NotificationType type = NotificationType.Info);
    IObservable<ToastNotification> Notifications { get; }
}

public record ToastNotification(string Message, NotificationType Type, Guid Id);