using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Axorith.Sdk.Actions;

/// <summary>
///     Default implementation for module actions.
/// </summary>
/// <remarks>
///     Initializes a new instance of the <see cref="Action" /> class.
/// </remarks>
public sealed class Action(string key, string label, bool isEnabled = true, string? settingKey = null) : IAction, IDisposable
{
    private readonly BehaviorSubject<string> _label = new(label);
    private readonly BehaviorSubject<bool> _isEnabled = new(isEnabled);
    private readonly Subject<Unit> _invoked = new();
    private Func<Task>? _asyncHandler;

    /// <summary>
    ///     Gets the unique identifier for this action.
    /// </summary>
    public string Key { get; } = key;

    /// <inheritdoc />
    public string? SettingKey { get; } = settingKey;

    /// <summary>
    ///     Gets an observable stream that emits the current label text for this action.
    /// </summary>
    public IObservable<string> Label => _label.AsObservable();

    /// <summary>
    ///     Gets an observable stream that emits the current enabled state of this action.
    /// </summary>
    public IObservable<bool> IsEnabled => _isEnabled.AsObservable();

    /// <inheritdoc />
    public string GetCurrentLabel() => _label.Value;

    /// <inheritdoc />
    public bool GetCurrentEnabled() => _isEnabled.Value;

    /// <summary>
    ///     Gets an observable stream that emits a signal each time this action is invoked.
    /// </summary>
    public IObservable<Unit> Invoked => _invoked.AsObservable();

    /// <summary>
    ///     Updates the action's display label dynamically.
    /// </summary>
    public void SetLabel(string label) => _label.OnNext(label);

    /// <summary>
    ///     Updates the action's enabled state dynamically.
    /// </summary>
    public void SetEnabled(bool enabled) => _isEnabled.OnNext(enabled);

    /// <inheritdoc />
    public void Invoke()
    {
        if (_isEnabled.Value)
        {
            _invoked.OnNext(Unit.Default);
        }
    }

    /// <inheritdoc />
    public async Task InvokeAsync()
    {
        if (!_isEnabled.Value)
        {
            return;
        }

        _invoked.OnNext(Unit.Default);

        if (_asyncHandler != null)
        {
            await _asyncHandler().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Registers an async handler that will be executed when InvokeAsync() is called.
    ///     This is used for long-running operations like OAuth login.
    /// </summary>
    public void OnInvokeAsync(Func<Task> handler) => _asyncHandler = handler;

    /// <summary>
    ///     Disposes the action and releases all resources.
    /// </summary>
    public void Dispose()
    {
        _label.Dispose();
        _isEnabled.Dispose();
        _invoked.Dispose();
    }
}
