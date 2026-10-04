using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Axorith.Client.ViewModels;

public partial class ShellViewModel : ReactiveObject
{
    public IServiceProvider Services { get; set; } = null!;

    [Reactive]
    public partial ReactiveObject? Content { get; set; }

    public void NavigateTo(ReactiveObject viewModel) => Content = viewModel;
}
