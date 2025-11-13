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

    // Factory method for creating configured instances
    public void Configure(string errorMessage, object? _ = null, Func<Task>? retryCallback = null)
    {
        ErrorMessage = errorMessage;
        _retryCallback = retryCallback;

        if (_retryCallback != null) RetryCommand = ReactiveCommand.CreateFromTask(RetryConnectionAsync);
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