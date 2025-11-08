using System.Windows.Input;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class ErrorViewModel : ReactiveObject
{
    private Func<Task>? _retryCallback;

    private string _errorMessage;

    public string ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private bool _isRetrying;

    public bool IsRetrying
    {
        get => _isRetrying;
        set => this.RaiseAndSetIfChanged(ref _isRetrying, value);
    }

    public ICommand? RetryCommand { get; private set; }

    // DI-friendly constructor
    public ErrorViewModel()
    {
        _errorMessage = string.Empty;
    }

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