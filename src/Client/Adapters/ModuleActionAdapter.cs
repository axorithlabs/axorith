using System.Diagnostics;
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
    private readonly Guid _moduleDefinitionId;

    public string Key { get; }
    public IObservable<string> Label { get; }
    public IObservable<bool> IsEnabled { get; }
    public IObservable<Unit> Invoked { get; }

    public ModuleActionAdapter(ModuleAction action, IModulesApi modulesApi, Guid moduleDefinitionId)
    {
        Key = action.Key;
        _modulesApi = modulesApi ?? throw new ArgumentNullException(nameof(modulesApi));
        _moduleDefinitionId = moduleDefinitionId;

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
                var result = await _modulesApi.InvokeActionAsync(_moduleDefinitionId, Key);
                if (result.Success)
                    _invokedSubject.OnNext(Unit.Default);
                else
                    // Log error but don't crash UI
                    Debug.WriteLine($"Action invocation failed: {result.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Action invocation error: {ex.Message}");
            }
        });
    }
}