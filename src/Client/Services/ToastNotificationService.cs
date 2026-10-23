using System.Reactive.Linq;
using System.Reactive.Subjects;
using Axorith.Client.Services.Abstractions;
using Axorith.Sdk.Services;

namespace Axorith.Client.Services;

public class ToastNotificationService : IToastNotificationService, IDisposable
{
    private readonly ISubject<ToastNotification> _notifications = Subject.Synchronize(new Subject<ToastNotification>());

    public IObservable<ToastNotification> Notifications => _notifications.AsObservable();

    public void Show(string message, NotificationType type = NotificationType.Info, string? category = null)
    {
        var notification = new ToastNotification(message, type, Guid.NewGuid(), category);
        _notifications.OnNext(notification);
    }

    public void Dispose()
    {
        _notifications.OnCompleted();
        GC.SuppressFinalize(this);
    }
}
