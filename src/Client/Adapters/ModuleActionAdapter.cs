using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Core.Telemetry;
using Axorith.Sdk.Actions;
using Axorith.Telemetry;

namespace Axorith.Client.Adapters;

/// <summary>
///     Adapts a remote ModuleAction into an IAction for UI binding.
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
    public string? SettingKey { get; } = action.SettingKey;
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
        _ = Task.Run(() => InvokeCoreAsync(throwOnError: false));
    }

    public async Task InvokeAsync()
    {
        await InvokeCoreAsync(throwOnError: true).ConfigureAwait(false);
    }

    private async Task InvokeCoreAsync(bool throwOnError)
    {
        try
        {
            var result = await modulesApi.InvokeDesignTimeActionAsync(moduleId, designTimeId, Key);
            if (result.Success)
                _invokedSubject.OnNext(Unit.Default);
            TrackActionInvocation(result.Success ? "success" : "failed");
        }
        catch (Exception ex)
        {
            TrackActionInvocation("failed");
            TrackActionError(ex);
            if (throwOnError)
                throw;
        }
    }

    private void TrackActionInvocation(string result) => telemetry?.TrackEvent("ModuleActionInvoked",
        new Dictionary<string, object?>
        {
            ["moduleId"] = moduleId,
            ["moduleName"] = moduleName,
            ["instanceId"] = designTimeId,
            ["actionKey"] = ProductAnalyticsProperties.NormalizeActionKey(Key),
            ["result"] = result
        });

    private void TrackActionError(Exception exception) => telemetry?.TrackError(exception, "module",
        "design_time_action", "error", handled: true, fatal: false, properties: new Dictionary<string, object?>
        {
            ["moduleId"] = moduleId,
            ["moduleName"] = moduleName,
            ["instanceId"] = designTimeId,
            ["actionKey"] = ProductAnalyticsProperties.NormalizeActionKey(Key)
        });

    public void Dispose()
    {
        _invokedSubject.Dispose();
        _labelSubject.Dispose();
        _enabledSubject.Dispose();
    }
}
