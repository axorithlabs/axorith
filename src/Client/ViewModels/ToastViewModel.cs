using Axorith.Client.Services.Abstractions;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class ToastViewModel : ReactiveObject
{
    public string Title { get; }
    public string Body { get; }
    public string DesktopTitle => Title.ToUpperInvariant();
    public Sdk.Services.NotificationType Type { get; }

    public ToastViewModel(ToastNotification model)
    {
        Type = model.Type;

        if (!string.IsNullOrEmpty(model.Message) && model.Message.Contains(": "))
        {
            var parts = model.Message.Split(": ", 2);
            Title = parts[0];
            Body = parts[1];
        }
        else
        {
            Title = "Notification";
            Body = model.Message;
        }
    }
}