using System.Windows.Input;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class ErrorViewModel : ReactiveObject
{
    private Func<Task>? _retryCallback;

    public string ErrorMessage
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public bool IsRetrying
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ICommand? RetryCommand { get; private set; }

    public void Configure(string errorMessage, Func<Task>? retryCallback = null)
    {
        ErrorMessage = errorMessage;
        _retryCallback = retryCallback;

        RetryCommand = _retryCallback != null
            ? ReactiveCommand.CreateFromTask(RetryConnectionAsync)
            : null;
    }

    private async Task RetryConnectionAsync()
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