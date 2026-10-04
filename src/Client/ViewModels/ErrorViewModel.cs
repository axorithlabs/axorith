using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Axorith.Client.ViewModels;

public partial class ErrorViewModel : ReactiveObject
{
    private Func<Task>? _retryCallback;

    [Reactive]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [Reactive]
    public partial bool IsRetrying { get; set; }

    public void Configure(string errorMessage, Func<Task> retryCallback)
    {
        ErrorMessage = errorMessage;
        _retryCallback = retryCallback;

    }

    [ReactiveCommand]
    private async Task Retry()
    {
        try
        {
            IsRetrying = true;
            ErrorMessage = "Retrying connection...\n\nPlease wait...";

            await _retryCallback!();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Retry failed:\n{ex.Message}";
        }
        finally
        {
            IsRetrying = false;
        }
    }
}
