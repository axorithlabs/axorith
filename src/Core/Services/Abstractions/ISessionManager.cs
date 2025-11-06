using Axorith.Core.Models;

namespace Axorith.Core.Services.Abstractions;

/// <summary>
///     Defines a service for managing the lifecycle of a deep work session.
/// </summary>
public interface ISessionManager : IAsyncDisposable
{
    /// <summary>
    ///     Gets a value indicating whether a session is currently active.
    /// </summary>
    bool IsSessionRunning { get; }

    /// <summary>
    ///     Gets the preset of the currently active session, if any.
    /// </summary>
    SessionPreset? ActiveSession { get; }

    /// <summary>
    ///     Occurs when a session has successfully started. The parameter is the ID of the started preset.
    /// </summary>
    event Action<Guid>? SessionStarted;

    /// <summary>
    ///     Occurs when a session has stopped. The parameter is the ID of the stopped preset.
    /// </summary>
    event Action<Guid>? SessionStopped;

    /// <summary>
    ///     Starts a new session using the specified preset.
    ///     Throws a <see cref="Axorith.Shared.Exceptions.SessionException" /> if a session is already running.
    /// </summary>
    /// <param name="preset">The session preset to execute.</param>
    Task StartSessionAsync(SessionPreset preset);

    /// <summary>
    ///     Stops the currently active session, performing cleanup for all modules.
    /// </summary>
    Task StopCurrentSessionAsync();
}