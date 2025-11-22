namespace Axorith.Shared.Platform;

/// <summary>
///     Defines a cross-platform service for showing system-level notifications (Toasts).
///     Designed to work in headless environments (Host) without requiring a UI message loop.
/// </summary>
public interface ISystemNotificationService
{
    /// <summary>
    ///     Shows a simple text notification to the user.
    /// </summary>
    /// <param name="title">The title of the notification.</param>
    /// <param name="message">The body text of the notification.</param>
    /// <param name="expiration">Optional expiration time after which the notification is removed from history.</param>
    Task ShowNotificationAsync(string title, string message, TimeSpan? expiration = null);
}