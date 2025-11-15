using System.Windows.Input;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class ErrorViewModel : ReactiveObject
{
    private Func<Task>? _retryCallback;
    private Func<Task>? _restartCallback;

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
    public ICommand? RestartHostCommand { get; private set; }

    public void Configure(string errorMessage, object? _ = null, Func<Task>? retryCallback = null,
        Func<Task>? restartCallback = null)
    {
        ErrorMessage = errorMessage;
        _retryCallback = retryCallback;
        _restartCallback = restartCallback;

        if (_retryCallback != null) RetryCommand = ReactiveCommand.CreateFromTask(RetryConnectionAsync);
        if (_restartCallback != null) RestartHostCommand = ReactiveCommand.CreateFromTask(ReloadHostAsync);
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

    private async Task ReloadHostAsync()
    {
        try
        {
            IsRetrying = true;
            ErrorMessage = "Restarting Host...\n\nPlease wait...";
            await _restartCallback!();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Restart failed:\n{ex.Message}";
        }
        finally
        {
            IsRetrying = false;
        }
    }
}