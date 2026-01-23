using Avalonia.Controls;
using Avalonia.Interactivity;
using Axorith.Client.ViewModels;

namespace Axorith.Client.Views;

public partial class SetupWizardView : UserControl
{
    public SetupWizardView()
    {
        InitializeComponent();
    }

    private void OnPresetTypeTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Border { DataContext: PresetTypeOption option } && DataContext is SetupWizardViewModel vm)
        {
            vm.ToggleSelectionCommand.Execute(option);
        }
    }
}
