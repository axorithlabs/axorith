using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class ShellViewModel : ReactiveObject
{
    private ReactiveObject? _content;
    public ReactiveObject? Content
    {
        get => _content;
        set => this.RaiseAndSetIfChanged(ref _content, value);
    }

    public void NavigateTo(ReactiveObject viewModel)
    {
        Content = viewModel;
    }
}