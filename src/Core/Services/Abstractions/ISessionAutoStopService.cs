namespace Axorith.Core.Services.Abstractions;

/// <summary>
///     Service for managing automatic session stop and transition to next preset.
/// </summary>
public interface ISessionAutoStopService : IAsyncDisposable
{
    /// <summary>
    ///     Starts the service and subscribes to session events.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Starts tracking a session with auto-stop configuration.
    /// </summary>
    /// <param name="sessionId">The ID of the session preset.</param>
    /// <param name="autoStopDuration">Duration after which the session should stop. Null means no auto-stop.</param>
    /// <param name="nextPresetId">ID of the preset to start after this session ends. Null means just stop.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartTrackingAsync(Guid sessionId, TimeSpan? autoStopDuration, Guid? nextPresetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stops tracking the current session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StopTrackingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the remaining time until auto-stop, or null if not tracking or no auto-stop configured.
    /// </summary>
    TimeSpan? GetTimeRemaining();
}