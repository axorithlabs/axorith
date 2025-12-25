namespace Axorith.Module.OBS;

/// <summary>
/// Interface for OBS WebSocket communication service.
/// </summary>
internal interface IObsWebSocketService : IDisposable
{
    /// <summary>
    /// Connects to OBS WebSocket server.
    /// </summary>
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from OBS WebSocket server.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Starts streaming in OBS.
    /// </summary>
    Task<bool> StartStreamingAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops streaming in OBS.
    /// </summary>
    Task<bool> StopStreamingAsync(CancellationToken ct = default);

    /// <summary>
    /// Starts recording in OBS.
    /// </summary>
    Task<bool> StartRecordingAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops recording in OBS.
    /// </summary>
    Task<bool> StopRecordingAsync(CancellationToken ct = default);

    /// <summary>
    /// Starts virtual camera in OBS.
    /// </summary>
    Task<bool> StartVirtualCameraAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops virtual camera in OBS.
    /// </summary>
    Task<bool> StopVirtualCameraAsync(CancellationToken ct = default);
}
