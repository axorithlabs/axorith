using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class LoadingViewModel : ReactiveObject
{
    public string Message
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "Connecting to Axorith Host...";
}