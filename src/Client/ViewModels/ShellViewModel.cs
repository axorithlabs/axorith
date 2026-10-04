using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Axorith.Client.ViewModels;

public class ShellViewModel : ReactiveObject
{
    public IServiceProvider Services { get; set; } = null!;

    [Reactive]
    public ReactiveObject? Content { get; set; }

    public void NavigateTo(ReactiveObject viewModel) => Content = viewModel;
}
