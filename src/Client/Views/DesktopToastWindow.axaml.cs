using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Axorith.Client.ViewModels;

namespace Axorith.Client.Views;

public partial class DesktopToastWindow : Window
{
    private readonly DispatcherTimer _autoCloseTimer;

    public DesktopToastWindow()
    {
        InitializeComponent();

        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(6)
        };
        _autoCloseTimer.Tick += (_, _) => Close();
    }

    public DesktopToastWindow(ToastViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _autoCloseTimer.Start();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        Close();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _autoCloseTimer.Stop();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _autoCloseTimer.Start();
    }
}