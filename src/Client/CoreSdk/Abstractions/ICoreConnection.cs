namespace Axorith.Client.CoreSdk.Abstractions;

public interface ICoreConnection : IAsyncDisposable
{
    IPresetsApi Presets { get; }

    ISessionsApi Sessions { get; }

    IModulesApi Modules { get; }

    IDiagnosticsApi Diagnostics { get; }

    ISchedulerApi Scheduler { get; }

    INotificationApi Notifications { get; }

    IUpdatesApi Updates { get; }

    ConnectionState State { get; }

    IObservable<ConnectionState> StateChanged { get; }

    Task ConnectAsync(CancellationToken ct = default);

    Task DisconnectAsync();
}

public enum ConnectionState
{
    Disconnected,

    Connecting,

    Connected,

    Reconnecting,

    Failed
}
