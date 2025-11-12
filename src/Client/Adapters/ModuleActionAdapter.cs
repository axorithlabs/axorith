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
    private readonly IModulesApi _modulesApi;
    private readonly Guid _moduleInstanceId;

    public string Key { get; }
    public IObservable<string> Label { get; }
    public IObservable<bool> IsEnabled { get; }
    public IObservable<Unit> Invoked { get; }

    public ModuleActionAdapter(ModuleAction action, IModulesApi modulesApi, Guid moduleInstanceId)
    {
        Key = action.Key;
        _modulesApi = modulesApi ?? throw new ArgumentNullException(nameof(modulesApi));
        _moduleInstanceId = moduleInstanceId;

        var labelSubject = new BehaviorSubject<string>(action.Label);
        var enabledSubject = new BehaviorSubject<bool>(action.IsEnabled);
        _invokedSubject = new Subject<Unit>();

        Label = labelSubject.AsObservable();
        IsEnabled = enabledSubject.AsObservable();
        Invoked = _invokedSubject.AsObservable();
    }

    public void Invoke()
    {
        // Fire-and-forget invocation via gRPC
        // Note: This creates a temporary instance for design-time actions (like OAuth login)
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _modulesApi.InvokeActionAsync(_moduleInstanceId, Key);
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
        var result = await _modulesApi.InvokeActionAsync(_moduleInstanceId, Key);
        if (result.Success)
            _invokedSubject.OnNext(Unit.Default);
    }
}