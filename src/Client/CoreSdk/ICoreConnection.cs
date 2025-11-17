namespace Axorith.Client.CoreSdk;

/// <summary>
///     Represents a connection to Axorith Core services.
///     Abstracts the transport mechanism (gRPC, in-process, etc.).
/// </summary>
public interface ICoreConnection : IAsyncDisposable
{
    /// <summary>
    ///     Access to preset management operations.
    /// </summary>
    IPresetsApi Presets { get; }

    /// <summary>
    ///     Access to session management operations and events.
    /// </summary>
    ISessionsApi Sessions { get; }

    /// <summary>
    ///     Access to module discovery and interaction.
    /// </summary>
    IModulesApi Modules { get; }

    /// <summary>
    ///     Access to diagnostics and health checks.
    /// </summary>
    IDiagnosticsApi Diagnostics { get; }

    /// <summary>
    ///     Current connection state.
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    ///     Observable stream of connection state changes.
    /// </summary>
    IObservable<ConnectionState> StateChanged { get; }

    /// <summary>
    ///     Establishes connection to Core services.
    ///     For gRPC: connects to host process.
    ///     For in-process: validates services are available.
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    ///     Disconnects from Core services.
    /// </summary>
    Task DisconnectAsync();
}

/// <summary>
///     Connection state enumeration.
/// </summary>
public enum ConnectionState
{
    /// <summary>
    ///     Not connected to Core services.
    /// </summary>
    Disconnected,

    /// <summary>
    ///     Attempting to establish connection.
    /// </summary>
    Connecting,

    /// <summary>
    ///     Successfully connected and ready for operations.
    /// </summary>
    Connected,

    /// <summary>
    ///     Connection lost, attempting to reconnect.
    /// </summary>
    Reconnecting,

    /// <summary>
    ///     Connection failed and reconnection attempts exhausted.
    /// </summary>
    Failed
}