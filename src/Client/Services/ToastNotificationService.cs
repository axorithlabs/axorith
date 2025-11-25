using System.Reactive.Linq;
using System.Reactive.Subjects;
using Axorith.Client.Services.Abstractions;
using Axorith.Sdk.Services;

namespace Axorith.Client.Services;

public class ToastNotificationService : IToastNotificationService
{
    private readonly Subject<ToastNotification> _notifications = new();

    public IObservable<ToastNotification> Notifications => _notifications.AsObservable();

    public void Show(string message, NotificationType type = NotificationType.Info)
    {
        var notification = new ToastNotification(message, type, Guid.NewGuid());
        _notifications.OnNext(notification);
    }
}