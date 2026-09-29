using Avalonia.Controls;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

/// <summary>
///     Holds the currently displayed ViewModel and the main window reference.
/// </summary>
public class ShellViewModel : ReactiveObject
{
    public IServiceProvider Services { get; set; } = null!;

    public ReactiveObject? Content
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    private Window? _mainWindow;

    public void SetMainWindow(Window window)
    {
        _mainWindow = window;
    }

    public Window GetMainWindow()
    {
        return _mainWindow ?? throw new InvalidOperationException("Main window not set");
    }

    public void NavigateTo(ReactiveObject viewModel)
    {
        Content = viewModel;
    }
}
