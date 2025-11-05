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
    /// 
    /// </summary>
    public string Key { get; }
    /// <summary>
    /// 
    /// </summary>
    public IObservable<string> Label => _label.AsObservable();
    /// <summary>
    /// 
    /// </summary>
    public IObservable<bool> IsEnabled => _isEnabled.AsObservable();
    /// <summary>
    /// 
    /// </summary>
    public IObservable<Unit> Invoked => _invoked.AsObservable();

    /// <summary>
    /// 
    /// </summary>
    /// <param name="key"></param>
    /// <param name="label"></param>
    /// <param name="isEnabled"></param>
    public Action(string key, string label, bool isEnabled = true)
    {
        Key = key;
        _label = new BehaviorSubject<string>(label);
        _isEnabled = new BehaviorSubject<bool>(isEnabled);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="label"></param>
    public void SetLabel(string label) => _label.OnNext(label);
    /// <summary>
    /// 
    /// </summary>
    /// <param name="enabled"></param>
    public void SetEnabled(bool enabled) => _isEnabled.OnNext(enabled);

    /// <summary>
    /// 
    /// </summary>
    public void Invoke()
    {
        if (_isEnabled.Value) _invoked.OnNext(Unit.Default);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="key"></param>
    /// <param name="label"></param>
    /// <param name="isEnabled"></param>
    /// <returns></returns>
    public static Action Create(string key, string label, bool isEnabled = true) => new(key, label, isEnabled);
}


