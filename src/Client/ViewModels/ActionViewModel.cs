using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using Axorith.Sdk.Actions;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

/// <summary>
///     ViewModel for a reactive action, bridging IAction from the SDK to the Avalonia UI.
///     Subscribes to action observables and exposes bindable properties for the View.
/// </summary>
public sealed class ActionViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    public string Key { get; }
    public IAction SourceAction { get; }

    public string Label
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public bool IsSuccess => Label == "Connected OK";

    public bool IsFailure => Label.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                             Label.Contains("Error", StringComparison.OrdinalIgnoreCase);

    public bool IsEnabled
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    public bool IsVisible
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

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

        action.Label
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
            .DisposeWith(_disposables);

        action.IsEnabled
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(
                v => IsEnabled = v,
                _ =>
                {
                    /* Ignore errors after module disposal */
                })
            .DisposeWith(_disposables);
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
