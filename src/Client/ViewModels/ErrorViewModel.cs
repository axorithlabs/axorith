using System.Windows.Input;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class ErrorViewModel : ReactiveObject
{
    private Func<Task>? _retryCallback;
    private Func<Task>? _updateCallback;

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
    public ICommand? UpdateAndRestartCommand { get; private set; }

    /// <summary>
    ///     Gets or sets the client version when a version conflict is detected.
    /// </summary>
    public string? ClientVersion
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    ///     Gets or sets the host version when a version conflict is detected.
    /// </summary>
    public string? HostVersion
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    ///     Gets whether this error represents a version conflict.
    /// </summary>
    public bool IsVersionConflict => !string.IsNullOrEmpty(ClientVersion) && !string.IsNullOrEmpty(HostVersion);

    /// <summary>
    ///     Gets or sets whether an update is currently in progress.
    /// </summary>
    public bool IsUpdating
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    ///     Gets or sets the update download progress percentage (0-100).
    /// </summary>
    public double UpdateProgress
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    ///     Configures the error view for a generic error with retry capability.
    /// </summary>
    public void Configure(string errorMessage, Func<Task> retryCallback)
    {
        ErrorMessage = errorMessage;
        _retryCallback = retryCallback;
        ClientVersion = null;
        HostVersion = null;

        RetryCommand = ReactiveCommand.CreateFromTask(RetryConnectionAsync);
        UpdateAndRestartCommand = null;
    }

    /// <summary>
    ///     Configures the error view for a version conflict error.
    ///     Shows both client and host versions with an update option.
    /// </summary>
    public void ConfigureVersionConflict(
        string clientVersion,
        string hostVersion,
        Func<Task>? updateCallback = null,
        Func<Task>? retryCallback = null)
    {
        ClientVersion = clientVersion;
        HostVersion = hostVersion;
        ErrorMessage = $"Client version {clientVersion} is incompatible with Host version {hostVersion}. Please update.";
        _retryCallback = retryCallback;
        _updateCallback = updateCallback;

        if (_updateCallback != null)
        {
            UpdateAndRestartCommand = ReactiveCommand.CreateFromTask(UpdateAndRestartAsync);
        }

        if (_retryCallback != null)
        {
            RetryCommand = ReactiveCommand.CreateFromTask(RetryConnectionAsync);
        }
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

    private async Task UpdateAndRestartAsync()
    {
        try
        {
            IsUpdating = true;
            ErrorMessage = "Checking for updates...\n\nPlease wait...";

            await _updateCallback!();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Update failed:\n{ex.Message}";
        }
        finally
        {
            IsUpdating = false;
        }
    }
}
