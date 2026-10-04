using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using Axorith.Sdk.Actions;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Axorith.Client.ViewModels;

public sealed class ActionViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    public string Key { get; }
    public IAction SourceAction { get; }

    [Reactive]
    public string Label { get; private set; } = string.Empty;

    public bool IsSuccess => Label == "Connected OK";

    public bool IsFailure => Label.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                             Label.Contains("Error", StringComparison.OrdinalIgnoreCase);

    [Reactive]
    public bool IsVisible { get; private set; } = true;

    private bool _isInline;

    public bool IsVisibleInActionsList => IsVisible && !_isInline;

    public void SetVisible(bool visible)
    {
        IsVisible = visible;
        this.RaisePropertyChanged(nameof(IsVisibleInActionsList));
    }

    public void SetInline(bool inline)
    {
        if (_isInline == inline)
            return;

        _isInline = inline;
        this.RaisePropertyChanged(nameof(IsVisibleInActionsList));
    }

    public ICommand InvokeCommand { get; }

    public ActionViewModel(IAction action)
    {
        SourceAction = action;
        Key = action.Key;

        var canExecute = action.IsEnabled
            .ObserveOn(RxApp.MainThreadScheduler)
            .Catch<bool, Exception>(_ => Observable.Return(false));

        InvokeCommand = ReactiveCommand.Create(action.Invoke, canExecute);

        _disposables.Add(action.Label
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(
                v =>
                {
                    Label = v;
                    this.RaisePropertyChanged(nameof(IsSuccess));
                    this.RaisePropertyChanged(nameof(IsFailure));
                },
                _ =>
                {
                    /* Ignore errors after module disposal */
                })
            );

    }

    public void Dispose() => _disposables.Dispose();
}
