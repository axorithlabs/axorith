using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Axorith.Sdk.Actions;

/// <summary>
///     Default implementation and factory for module actions.
/// </summary>
public sealed class Action : IAction
{
    private readonly BehaviorSubject<string> _label;
    private readonly BehaviorSubject<bool> _isEnabled;
    private readonly Subject<Unit> _invoked = new();

    /// <summary>
    ///     Gets the unique identifier for this action.
    /// </summary>
    public string Key { get; }

    /// <summary>
    ///     Gets an observable stream that emits the current label text for this action.
    /// </summary>
    public IObservable<string> Label => _label.AsObservable();

    /// <summary>
    ///     Gets an observable stream that emits the current enabled state of this action.
    /// </summary>
    public IObservable<bool> IsEnabled => _isEnabled.AsObservable();

    /// <summary>
    ///     Gets an observable stream that emits a signal each time this action is invoked.
    /// </summary>
    public IObservable<Unit> Invoked => _invoked.AsObservable();

    /// <summary>
    ///     Initializes a new instance of the <see cref="Action" /> class.
    /// </summary>
    /// <param name="key">The unique identifier for this action.</param>
    /// <param name="label">The display label for the action button.</param>
    /// <param name="isEnabled">Whether the action is initially enabled.</param>
    public Action(string key, string label, bool isEnabled = true)
    {
        Key = key;
        _label = new BehaviorSubject<string>(label);
        _isEnabled = new BehaviorSubject<bool>(isEnabled);
    }

    /// <summary>
    ///     Updates the action's display label dynamically.
    /// </summary>
    /// <param name="label">The new label text to display.</param>
    public void SetLabel(string label)
    {
        _label.OnNext(label);
    }

    /// <summary>
    ///     Updates the action's enabled state dynamically.
    /// </summary>
    /// <param name="enabled">True to enable the action, false to disable it.</param>
    public void SetEnabled(bool enabled)
    {
        _isEnabled.OnNext(enabled);
    }

    /// <summary>
    ///     Invokes the action if it is currently enabled, notifying all subscribers.
    /// </summary>
    public void Invoke()
    {
        if (_isEnabled.Value) _invoked.OnNext(Unit.Default);
    }

    /// <summary>
    ///     Factory method to create a new action instance.
    /// </summary>
    /// <param name="key">The unique identifier for this action.</param>
    /// <param name="label">The display label for the action button.</param>
    /// <param name="isEnabled">Whether the action is initially enabled.</param>
    /// <returns>A new <see cref="Action" /> instance.</returns>
    public static Action Create(string key, string label, bool isEnabled = true)
    {
        return new Action(key, label, isEnabled);
    }
}