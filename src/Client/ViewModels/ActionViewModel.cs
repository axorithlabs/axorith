using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Windows.Input;
using Axorith.Sdk.Actions;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public sealed class ActionViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly IAction _action;

    private string _label = string.Empty;

    public string Label
    {
        get => _label;
        private set => this.RaiseAndSetIfChanged(ref _label, value);
    }

    private bool _isEnabled = true;

    public bool IsEnabled
    {
        get => _isEnabled;
        private set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
    }

    public ICommand InvokeCommand { get; }

    public ActionViewModel(IAction action)
    {
        _action = action;

        // Create ReactiveCommand with IsEnabled observable on UI thread
        // This ensures CanExecuteChanged fires on the correct thread
        var canExecute = _action.IsEnabled
            .ObserveOn(RxApp.MainThreadScheduler)
            .Catch<bool, Exception>(_ => Observable.Return(false));

        InvokeCommand = ReactiveCommand.Create(_action.Invoke, canExecute);

        _action.Label
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(
                v => Label = v,
                ex =>
                {
                    /* Ignore errors after module disposal */
                })
            .DisposeWith(_disposables);

        _action.IsEnabled
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(
                v => IsEnabled = v,
                ex =>
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