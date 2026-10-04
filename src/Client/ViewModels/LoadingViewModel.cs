using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Axorith.Client.ViewModels;

public partial class LoadingViewModel : ReactiveObject
{
    [Reactive]
    public partial string Message { get; set; } = "Connecting to Axorith Host...";

    [Reactive]
    public partial string? SubMessage { get; set; }
}
