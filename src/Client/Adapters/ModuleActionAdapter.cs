using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Sdk.Actions;
using Axorith.Telemetry;

namespace Axorith.Client.Adapters;

/// <summary>
///     Adapts a ModuleAction from gRPC into an IAction for UI binding.
///     Actions are invoked on the live module instance via gRPC.
/// </summary>
internal class ModuleActionAdapter(
    ModuleAction action,
    IModulesApi modulesApi,
    Guid moduleId,
    Guid designTimeId,
    string moduleName,
    ITelemetryService? telemetry = null)
    : IAction, IDisposable
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

    public void SetLabel(string label)
    {
        _labelSubject.OnNext(label);
    }

    public void SetEnabled(bool enabled)
    {
        _enabledSubject.OnNext(enabled);
    }

    public void Invoke()
    {
        // Fire-and-forget invocation via gRPC against the design-time sandbox instance
        // (keyed by the configured module InstanceId).
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await modulesApi.InvokeDesignTimeActionAsync(moduleId, designTimeId, Key);

                if (result.Success)
                {
                    _invokedSubject.OnNext(Unit.Default);
                }
                TrackActionInvocation(result.Success ? "success" : "failed");
            }
            catch (Exception ex)
            {
                // Adapter has no logger - silently ignore action invocation errors
                // Errors are logged on the Host side
                TrackActionInvocation("failed");
                TrackActionError(ex);
            }
        });
    }

    public async Task InvokeAsync()
    {
        // Invoke action via gRPC and wait for completion
        // Used for actions that require async completion (e.g., OAuth login)
        try
        {
            var result = await modulesApi.InvokeDesignTimeActionAsync(moduleId, designTimeId, Key);

            if (result.Success)
            {
                _invokedSubject.OnNext(Unit.Default);
            }
            TrackActionInvocation(result.Success ? "success" : "failed");
        }
        catch (Exception ex)
        {
            TrackActionInvocation("failed");
            TrackActionError(ex);
            throw;
        }
    }

    private void TrackActionInvocation(string result) => telemetry?.TrackEvent("ModuleActionInvoked",
        new Dictionary<string, object?>
        {
            ["moduleId"] = moduleId,
            ["moduleName"] = moduleName,
            ["instanceId"] = designTimeId,
            ["actionKey"] = Key,
            ["result"] = result
        });

    private void TrackActionError(Exception exception) => telemetry?.TrackError(exception, "module",
        "design_time_action", "error", handled: true, fatal: false, properties: new Dictionary<string, object?>
        {
            ["moduleId"] = moduleId,
            ["moduleName"] = moduleName,
            ["instanceId"] = designTimeId,
            ["actionKey"] = Key
        });

    public void Dispose()
    {
        _invokedSubject.Dispose();
        _labelSubject.Dispose();
        _enabledSubject.Dispose();
    }
}
