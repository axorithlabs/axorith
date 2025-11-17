using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Axorith.Sdk.Actions;

/// <summary>
///     Default implementation and factory for module actions.
/// </summary>
/// <remarks>
///     Initializes a new instance of the <see cref="Action" /> class.
/// </remarks>
/// <param name="key">The unique identifier for this action.</param>
/// <param name="label">The display label for the action button.</param>
/// <param name="isEnabled">Whether the action is initially enabled.</param>
public sealed class Action(string key, string label, bool isEnabled = true) : IAction
{
    private readonly BehaviorSubject<string> _label = new(label);
    private readonly BehaviorSubject<bool> _isEnabled = new(isEnabled);
    private readonly Subject<Unit> _invoked = new();
    private Func<Task>? _asyncHandler;

    /// <summary>
    ///     Gets the unique identifier for this action.
    /// </summary>
    public string Key { get; } = key;

    /// <summary>
    ///     Gets an observable stream that emits the current label text for this action.
    /// </summary>
    public IObservable<string> Label => _label.AsObservable();

    /// <summary>
    ///     Gets an observable stream that emits the current enabled state of this action.
    /// </summary>
    public IObservable<bool> IsEnabled => _isEnabled.AsObservable();

    /// <inheritdoc />
    public string GetCurrentLabel()
    {
        return _label.Value;
    }

    /// <inheritdoc />
    public bool GetCurrentEnabled()
    {
        return _isEnabled.Value;
    }

    /// <summary>
    ///     Gets an observable stream that emits a signal each time this action is invoked.
    /// </summary>
    public IObservable<Unit> Invoked => _invoked.AsObservable();

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
    ///     Invokes the action asynchronously and waits for completion.
    ///     If an async handler is registered, waits for it to complete.
    ///     Otherwise, invokes synchronously and returns immediately.
    /// </summary>
    public async Task InvokeAsync()
    {
        if (!_isEnabled.Value) return;

        _invoked.OnNext(Unit.Default);

        if (_asyncHandler != null)
            await _asyncHandler().ConfigureAwait(false);
    }

    /// <summary>
    ///     Registers an async handler that will be executed when InvokeAsync() is called.
    ///     This is used for long-running operations like OAuth login.
    /// </summary>
    /// <param name="handler">The async task to execute on invocation.</param>
    public void OnInvokeAsync(Func<Task> handler)
    {
        _asyncHandler = handler;
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