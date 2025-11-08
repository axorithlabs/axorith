using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class LoadingViewModel : ReactiveObject
{
    private string _message = "Connecting to Axorith Host...";

    public string Message
    {
        get => _message;
        set => this.RaiseAndSetIfChanged(ref _message, value);
    }
}