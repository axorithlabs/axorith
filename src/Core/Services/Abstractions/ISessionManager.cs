using Axorith.Core.Models;

namespace Axorith.Core.Services.Abstractions;

/// <summary>
/// Defines a service for managing the lifecycle of a deep work session.
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// Gets a value indicating whether a session is currently active.
    /// </summary>
    bool IsSessionRunning { get; }

    /// <summary>
    /// Occurs when a session has successfully started.
    /// </summary>
    event Action? SessionStarted;

    /// <summary>
    /// Occurs when a session has stopped.
    /// </summary>
    event Action? SessionStopped;

    /// <summary>
    /// Starts a new session using the specified preset.
    /// If a session is already running, this will throw an exception.
    /// </summary>
    /// <param name="preset">The session preset to execute.</param>
    Task StartSessionAsync(SessionPreset preset);

    /// <summary>
    /// Stops the currently active session.
    /// </summary>
    Task StopCurrentSessionAsync();
}