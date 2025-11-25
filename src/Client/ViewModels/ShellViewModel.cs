using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Avalonia.Threading;
using Axorith.Client.Services.Abstractions;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

/// <summary>
///     The main ViewModel for the application shell.
///     It holds the currently displayed content (page/view) and manages global overlays like Toasts.
/// </summary>
public class ShellViewModel : ReactiveObject
{
    /// <summary>
    ///     Gets or sets the current ViewModel to be displayed in the main content area of the window.
    /// </summary>
    public ReactiveObject? Content
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    ///     Collection of active toast notifications to display.
    /// </summary>
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    public ShellViewModel(IToastNotificationService toastService)
    {
        toastService.Notifications
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(AddToast);
    }

    /// <summary>
    ///     Navigates to a new ViewModel, setting it as the current content.
    /// </summary>
    /// <param name="viewModel">The ViewModel of the page to navigate to.</param>
    public void NavigateTo(ReactiveObject viewModel)
    {
        Content = viewModel;
    }

    private void AddToast(ToastNotification notification)
    {
        var vm = new ToastViewModel(notification);
        Toasts.Add(vm);

        // Auto-dismiss after 3 seconds
        Observable.Timer(TimeSpan.FromSeconds(3))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => RemoveToast(vm));
    }

    private void RemoveToast(ToastViewModel vm)
    {
        if (Toasts.Contains(vm))
        {
            Toasts.Remove(vm);
        }
    }
}

public class ToastViewModel(ToastNotification model) : ReactiveObject
{
    public string Message => model.Message;
    public Sdk.Services.NotificationType Type => model.Type;
}