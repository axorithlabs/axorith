using System.Reactive;

namespace Axorith.Sdk.Actions;

/// <summary>
///     Represents a non-persisted user action (command) exposed by a module for the Client to render and invoke.
///     Unlike settings, actions are not serialized into presets.
/// </summary>
public interface IAction
{
    /// <summary>
    ///     Unique, machine-readable key for this action.
    /// </summary>
    string Key { get; }

    /// <summary>
    ///     Reactive label displayed in the UI.
    /// </summary>
    IObservable<string> Label { get; }

    /// <summary>
    ///     Controls whether the action can be invoked.
    /// </summary>
    IObservable<bool> IsEnabled { get; }

    /// <summary>
    ///     Gets the current label of the action.
    /// </summary>
    /// <returns>The current label of the action.</returns>
    string GetCurrentLabel();

    /// <summary>
    ///     Gets the current enabled state of the action.
    /// </summary>
    /// <returns>True if the action is enabled; otherwise, false.</returns>
    bool GetCurrentEnabled();

    /// <summary>
    ///     Emits a value each time the action is invoked by the user.
    /// </summary>
    IObservable<Unit> Invoked { get; }

    /// <summary>
    ///     Programmatically triggers the action invocation synchronously (fire-and-forget).
    ///     Use InvokeAsync for actions that require async completion (e.g., OAuth login).
    /// </summary>
    void Invoke();

    /// <summary>
    ///     Programmatically triggers the action invocation and waits for completion.
    ///     Returns a Task that completes when the action handler finishes execution.
    ///     For actions without async work, this returns a completed Task immediately.
    /// </summary>
    Task InvokeAsync();
}