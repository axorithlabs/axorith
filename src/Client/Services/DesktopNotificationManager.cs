using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
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
    private Screens? _screens;
    private bool _repositionPending;

    private const int MarginBottom = 20;
    private const int MarginRight = 20;
    private const int Spacing = 12;

    public void Initialize()
    {
        _screens = desktop.MainWindow?.Screens;
        if (_screens != null)
            _screens.Changed += QueueReposition;
        if (desktop.MainWindow != null)
            desktop.MainWindow.PositionChanged += QueueReposition;

        _subscription = toastService.Notifications
            .ObserveOn(AvaloniaScheduler.Instance)
            .Subscribe(ShowDesktopToast);
    }

    private void ShowDesktopToast(ToastNotification notification)
    {
        var vm = new ToastViewModel(notification);
        var window = new DesktopToastWindow(vm);

        if (GetTargetScreen() is { } screen)
        {
            ConstrainToScreen(window, screen);
            window.Position = screen.WorkingArea.Position;
        }

        _openWindows.Add(window);
        window.Closed += (_, _) =>
        {
            window.Resized -= QueueReposition;
            window.ScalingChanged -= QueueReposition;
            if (_openWindows.Remove(window))
                RepositionWindows();
        };

        window.Show();
        window.Resized += QueueReposition;
        window.ScalingChanged += QueueReposition;
        RepositionWindows();
    }

    private Screen? GetTargetScreen()
    {
        var mainWindow = desktop.MainWindow;
        var screen = (mainWindow?.PlatformImpl != null ? _screens?.ScreenFromWindow(mainWindow) : null)
                     ?? _screens?.Primary ?? _screens?.All.FirstOrDefault();
        return screen is { WorkingArea.Width: > 0, WorkingArea.Height: > 0, Scaling: > 0 } &&
               double.IsFinite(screen.Scaling) ? screen : null;
    }

    private static void ConstrainToScreen(DesktopToastWindow window, Screen screen)
    {
        window.MaxWidth = Math.Max(1, screen.WorkingArea.Width / screen.Scaling - MarginRight * 2);
        window.Width = Math.Min(400, window.MaxWidth);
        window.MaxHeight = Math.Max(1, screen.WorkingArea.Height / screen.Scaling - MarginBottom * 2);
    }

    private void QueueReposition(object? sender, EventArgs e)
    {
        if (_repositionPending)
            return;

        _repositionPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _repositionPending = false;
            RepositionWindows();
        }, DispatcherPriority.Loaded);
    }

    private void RepositionWindows()
    {
        if (_openWindows.Count == 0 || GetTargetScreen() is not { } screen)
            return;

        var workingArea = screen.WorkingArea;
        var marginBottom = (int)Math.Ceiling(MarginBottom * screen.Scaling);
        var marginRight = (int)Math.Ceiling(MarginRight * screen.Scaling);
        var spacing = (int)Math.Ceiling(Spacing * screen.Scaling);
        var currentY = workingArea.Bottom - marginBottom;

        for (var i = _openWindows.Count - 1; i >= 0; i--)
        {
            var window = _openWindows[i];

            if (window.Screens.ScreenFromWindow(window) != screen)
                window.Position = workingArea.Position;
            ConstrainToScreen(window, screen);
            window.UpdateLayout();

            // Screen coordinates are physical pixels; layout sizes and margins are DIPs.
            var size = PixelSize.FromSize(window.Bounds.Size, screen.Scaling);
            var x = workingArea.Right - size.Width - marginRight;
            var y = currentY - size.Height;
            if (y < workingArea.Y + marginBottom)
            {
                _openWindows.RemoveAt(i);
                window.Close();
                continue;
            }

            var position = new PixelPoint(x, y);
            if (window.Position != position)
                window.Position = position;

            currentY = y - spacing;
        }
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        if (_screens != null)
            _screens.Changed -= QueueReposition;
        if (desktop.MainWindow != null)
            desktop.MainWindow.PositionChanged -= QueueReposition;

        var windows = _openWindows.ToArray();
        _openWindows.Clear();
        foreach (var window in windows)
        {
            window.Close();
        }
    }
}
