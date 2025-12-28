namespace Axorith.Shared.Platform;

/// <summary>
///     Platform-specific service for managing application auto-start behavior.
/// </summary>
public interface IAutoStartManager
{
    /// <summary>
    ///     Gets whether auto-start is currently enabled.
    /// </summary>
    bool IsAutoStartEnabled { get; }

    /// <summary>
    ///     Enables auto-start for the application.
    /// </summary>
    /// <param name="startMinimized">If true, starts minimized to tray.</param>
    /// <returns>True if successful.</returns>
    bool EnableAutoStart(bool startMinimized = true);

    /// <summary>
    ///     Disables auto-start for the application.
    /// </summary>
    /// <returns>True if successful.</returns>
    bool DisableAutoStart();

    /// <summary>
    ///     Gets whether the current auto-start configuration starts minimized.
    /// </summary>
    bool IsStartMinimized { get; }
}
