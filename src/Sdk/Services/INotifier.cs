namespace Axorith.Sdk.Services;

/// <summary>
///     Defines the types of notifications available in the system.
/// </summary>
public enum NotificationType
{
    /// <summary>
    ///     Neutral information.
    /// </summary>
    Info,

    /// <summary>
    ///     Positive confirmation (e.g., "Saved").
    /// </summary>
    Success,

    /// <summary>
    ///     Non-critical issue (e.g., "Device not found, retrying").
    /// </summary>
    Warning,

    /// <summary>
    ///     Critical issue or operation failure.
    /// </summary>
    Error
}

/// <summary>
///     Provides a unified way for modules to send notifications to the user,
///     regardless of whether they are displayed in the App UI (Toast) or the OS (System).
/// </summary>
public interface INotifier
{
    /// <summary>
    ///     Shows a transient, in-app notification (Toast).
    ///     These are intended for immediate feedback when the user is interacting with the application.
    ///     If no client UI is connected, these messages may be discarded or logged.
    /// </summary>
    void ShowToast(string message, NotificationType type = NotificationType.Info);

    /// <summary>Shows a toast under its originating feature category.</summary>
    void ShowToast(string message, NotificationType type, string? category) => ShowToast(message, type);

    /// <summary>
    ///     Shows a persistent, system-level notification (e.g., Windows Action Center).
    ///     These are intended for background events when the user might not be looking at the application.
    /// </summary>
    Task ShowSystemAsync(string title, string message, TimeSpan? expiration = null);

    /// <summary>Shows a persistent notification under its originating feature category.</summary>
    Task ShowSystemAsync(string title, string message, string? category, TimeSpan? expiration = null) =>
        ShowSystemAsync(title, message, expiration);
}
