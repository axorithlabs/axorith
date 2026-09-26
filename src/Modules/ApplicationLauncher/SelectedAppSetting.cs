using System.Reactive.Subjects;
using Axorith.Sdk.Settings;

namespace Axorith.Module.ApplicationLauncher;

// Keeps a child module's visibility separate from the visibility exposed by Application Launcher.
internal sealed class SelectedAppSetting : ISetting, IDisposable
{
    private readonly ISetting _inner;
    private readonly BehaviorSubject<bool> _visibility = new(false);
    private readonly IDisposable _subscription;
    private readonly Lock _sync = new();
    private bool _selected;
    private bool _childVisible;

    public SelectedAppSetting(ISetting inner)
    {
        _inner = inner;
        _childVisible = inner.GetCurrentVisibility();
        _subscription = inner.IsVisible.Subscribe(visible =>
        {
            lock (_sync)
            {
                _childVisible = visible;
                _visibility.OnNext(_selected && visible);
            }
        });
    }

    public void SetSelected(bool selected)
    {
        lock (_sync)
        {
            _selected = selected;
            _visibility.OnNext(selected && _childVisible);
        }
    }

    public string Key => _inner.Key;
    public IObservable<string> Label => _inner.Label;
    public string? Description => _inner.Description;
    public SettingControlType ControlType => _inner.ControlType;
    public Type ValueType => _inner.ValueType;
    public SettingPersistence Persistence => _inner.Persistence;
    public IObservable<object?> ValueAsObject => _inner.ValueAsObject;
    public IObservable<bool> IsVisible => _visibility;
    public IObservable<bool> IsReadOnly => _inner.IsReadOnly;
    public IObservable<IReadOnlyList<KeyValuePair<string, string>>>? Choices => _inner.Choices;
    public string? Filter => _inner.Filter;
    public bool HasHistory => _inner.HasHistory;
    public object? GetCurrentValueAsObject() => _inner.GetCurrentValueAsObject();
    public string GetValueAsString() => _inner.GetValueAsString();
    public void SetValueFromObject(object? value) => _inner.SetValueFromObject(value);
    public void SetValueFromString(string? value) => _inner.SetValueFromString(value);
    public void SetVisibility(bool isVisible) => _inner.SetVisibility(isVisible);
    public string GetCurrentLabel() => _inner.GetCurrentLabel();
    public bool GetCurrentVisibility()
    {
        lock (_sync)
            return _selected && _childVisible;
    }
    public bool GetCurrentReadOnly() => _inner.GetCurrentReadOnly();
    public IReadOnlyList<KeyValuePair<string, string>>? GetCurrentChoices() => _inner.GetCurrentChoices();

    public void Dispose()
    {
        _subscription.Dispose();
        _visibility.Dispose();
    }
}
