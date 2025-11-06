using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;

namespace Axorith.Sdk;

/// <summary>
///     The main contract that every Axorith module must implement.
///     A module represents a disposable, stateful component that executes tasks during a session.
///     A new instance of a module is created by the Core for each session it participates in,
///     and its <see cref="IDisposable.Dispose" /> method is called upon session completion.
///     IMPORTANT: Module constructors should be lightweight and side-effect free.
///     - Do NOT perform heavy initialization, network calls, or file I/O in the constructor.
///     - Use InitializeAsync() for lazy loading of resources (called on design-time).
///     - Use OnSessionStartAsync() for session-specific startup logic.
///     - Multiple instances may be created for editing and runtime, so constructors must be cheap.
/// </summary>
public interface IModule : IDisposable
{
    /// <summary>
    ///     Gets the list of all available settings for this module.
    /// </summary>
    /// <returns>A read-only list of <see cref="ISetting" /> definitions.</returns>
    IReadOnlyList<ISetting> GetSettings();

    /// <summary>
    ///     Gets the list of user-invokable actions for this module (non-persisted in presets).
    /// </summary>
    IReadOnlyList<IAction> GetActions();

    /// <summary>
    ///     Optional. Asynchronously initializes heavy resources like caches or discovery.
    ///     Called when module instance is created for editing or viewing (before session start).
    ///     Default implementation does nothing. Override to implement lazy loading.
    /// </summary>
    /// <param name="cancellationToken">A token to signal cancellation.</param>
    Task InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Asynchronously validates the provided user settings.
    /// </summary>
    /// <param name="cancellationToken">A token to signal that the validation should be cancelled.</param>
    Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Optional. Gets the type of a custom Avalonia UserControl for rendering this module's settings.
    ///     If this is not null, the Client will ignore GetSettings() and render this control instead.
    ///     The DataContext for this control will be an object provided by GetSettingsViewModel().
    /// </summary>
    Type? CustomSettingsViewType { get; }

    /// <summary>
    ///     Optional. Gets an object that will be used as the DataContext for the CustomSettingsView.
    ///     This is only called if CustomSettingsViewType is not null.
    /// </summary>
    object? GetSettingsViewModel();

    /// <summary>
    ///     The asynchronous method that is called when a session starts.
    /// </summary>
    /// <param name="cancellationToken">A token to signal that the start-up process should be cancelled.</param>
    Task OnSessionStartAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     The asynchronous method that is called when a session ends.
    ///     This is where the module should clean up its resources.
    /// </summary>
    Task OnSessionEndAsync();
}