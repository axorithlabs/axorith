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
internal class ModuleActionAdapter(ModuleAction action, IModulesApi modulesApi, Guid designTimeId)
    : IAction
{
    private readonly Subject<Unit> _invokedSubject = new();
    private readonly BehaviorSubject<string> _labelSubject = new(action.Label);
    private readonly BehaviorSubject<bool> _enabledSubject = new(action.IsEnabled);

    public string Key { get; } = action.Key;
    public IObservable<string> Label => _labelSubject.AsObservable();
    public IObservable<bool> IsEnabled => _enabledSubject.AsObservable();
    public IObservable<Unit> Invoked => _invokedSubject.AsObservable();

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
        // Fire-and-forget invocation via gRPC against the design-time sandbox instance
        // (keyed by the configured module InstanceId).

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await modulesApi.InvokeDesignTimeActionAsync(designTimeId, Key);

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
        var result = await modulesApi.InvokeDesignTimeActionAsync(designTimeId, Key);

        if (result.Success)
            _invokedSubject.OnNext(Unit.Default);
    }
}