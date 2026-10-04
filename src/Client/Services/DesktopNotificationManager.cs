using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Axorith.Client.Services.Abstractions;
using Axorith.Client.ViewModels;
using Axorith.Client.Views;
using ReactiveUI.Avalonia;

namespace Axorith.Client.Services;

public class DesktopNotificationManager(
    IToastNotificationService toastService,
    IClassicDesktopStyleApplicationLifetime desktop)
    : IDisposable
{
    private readonly List<DesktopToastWindow> _openWindows = [];
    private IDisposable? _subscription;

    private const int MarginBottom = 20;
    private const int MarginRight = 20;
    private const int Spacing = 12;

    public void Initialize()
    {
        _subscription = toastService.Notifications
            .ObserveOn(AvaloniaScheduler.Instance)
            .Subscribe(ShowDesktopToast);
    }


    private void ShowDesktopToast(ToastNotification notification)
    {
        var vm = new ToastViewModel(notification);
        var window = new DesktopToastWindow(vm);

        _openWindows.Add(window);
        RepositionWindows();

        window.Closed += (_, _) =>
        {
            _openWindows.Remove(window);
            RepositionWindows();
        };

        window.Show();
    }

    private void RepositionWindows()
    {
        var screen = desktop.MainWindow?.Screens.Primary ?? desktop.MainWindow?.Screens.All.FirstOrDefault();

        if (screen == null)
        {
            return;
        }

        var workingArea = screen.WorkingArea;
        var currentY = workingArea.Bottom - MarginBottom;

        for (var i = _openWindows.Count - 1; i >= 0; i--)
        {
            var window = _openWindows[i];

            var height = window.Bounds.Height > 0 ? window.Bounds.Height : 140;

            var x = workingArea.Right - window.Width - MarginRight;
            var y = currentY - height;

            window.Position = new PixelPoint((int)x, (int)y);

            currentY = (int)(y - Spacing);
        }
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        foreach (var window in _openWindows.ToList())
        {
            window.Close();
        }

        _openWindows.Clear();
    }
}
