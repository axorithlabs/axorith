using Avalonia.Controls;
using Axorith.Client.ViewModels;

namespace Axorith.Client.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is ShellViewModel shell)
            {
                shell.SetMainWindow(this);
            }
        };
    }
}
