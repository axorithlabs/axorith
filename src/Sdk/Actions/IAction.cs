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
    ///     Emits a value each time the action is invoked by the user.
    /// </summary>
    IObservable<Unit> Invoked { get; }

    /// <summary>
    ///     Programmatically triggers the action invocation.
    /// </summary>
    void Invoke();
}