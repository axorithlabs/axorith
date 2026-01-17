using Avalonia.Controls;
using Axorith.Client.ViewModels;

namespace Axorith.Client.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        TitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };

        TitleBar.DoubleTapped += (_, _) =>
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is ShellViewModel shell)
            {
                shell.SetMainWindow(this);
            }
        };
    }
}