using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Axorith.Client.CoreSdk;
using Axorith.Sdk.Actions;

namespace Axorith.Client.Adapters;

/// <summary>
///     Adapts a ModuleAction from gRPC into an IAction for UI binding.
///     Actions are invoked on the live module instance via gRPC.
/// </summary>
internal class ModuleActionAdapter : IAction
{
    private readonly Subject<Unit> _invokedSubject;
    private readonly BehaviorSubject<string> _labelSubject;
    private readonly BehaviorSubject<bool> _enabledSubject;

    private readonly IModulesApi _modulesApi;
    private readonly Guid _moduleId;

    public string Key { get; }
    public IObservable<string> Label => _labelSubject.AsObservable();
    public IObservable<bool> IsEnabled => _enabledSubject.AsObservable();
    public IObservable<Unit> Invoked => _invokedSubject.AsObservable();

    public ModuleActionAdapter(ModuleAction action, IModulesApi modulesApi, Guid moduleId)
    {
        Key = action.Key;
        _modulesApi = modulesApi;
        _moduleId = moduleId;

        _labelSubject = new BehaviorSubject<string>(action.Label);
        _enabledSubject = new BehaviorSubject<bool>(action.IsEnabled);
        _invokedSubject = new Subject<Unit>();
    }

    public string GetCurrentLabel()
    {
        return _labelSubject.Value;
    }

    public bool GetCurrentEnabled()
    {
        return _enabledSubject.Value;
    }

    public void Invoke()
    {
        // Fire-and-forget invocation via gRPC
        // Note: This creates a temporary instance for design-time actions (like OAuth login)
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _modulesApi.InvokeDesignTimeActionAsync(_moduleId, Key);

                if (result.Success)
                    _invokedSubject.OnNext(Unit.Default);
            }
            catch (Exception)
            {
                // ignored
            }
        });
    }

    public async Task InvokeAsync()
    {
        // Invoke action via gRPC and wait for completion
        // Used for actions that require async completion (e.g., OAuth login)
        var result = await _modulesApi.InvokeDesignTimeActionAsync(_moduleId, Key);

        if (result.Success)
            _invokedSubject.OnNext(Unit.Default);
    }
}