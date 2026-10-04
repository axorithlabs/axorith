using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Axorith.Client.ViewModels;

public class LoadingViewModel : ReactiveObject
{
    [Reactive]
    public string Message { get; set; } = "Connecting to Axorith Host...";

    [Reactive]
    public string? SubMessage { get; set; }
}
