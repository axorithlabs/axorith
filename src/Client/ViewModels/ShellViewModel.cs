using ReactiveUI;

namespace Axorith.Client.ViewModels;

/// <summary>
/// The main ViewModel for the application shell.
/// It holds the currently displayed content (page/view).
/// </summary>
public class ShellViewModel : ReactiveObject
{
    private ReactiveObject? _content;
    
    /// <summary>
    /// Gets or sets the current ViewModel to be displayed in the main content area of the window.
    /// </summary>
    public ReactiveObject? Content
    {
        get => _content;
        set => this.RaiseAndSetIfChanged(ref _content, value);
    }

    /// <summary>
    /// Navigates to a new ViewModel, setting it as the current content.
    /// </summary>
    /// <param name="viewModel">The ViewModel of the page to navigate to.</param>
    public void NavigateTo(ReactiveObject viewModel)
    {
        Content = viewModel;
    }
}