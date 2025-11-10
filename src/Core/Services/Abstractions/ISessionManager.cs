using Axorith.Core.Models;
using Axorith.Sdk;

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
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    Task StartSessionAsync(SessionPreset preset, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stops the currently active session, performing cleanup for all modules.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    Task StopCurrentSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the active instance of a module by its module ID.
    ///     Returns null if the session is not running or the module is not active.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module definition.</param>
    /// <returns>The active module instance, or null if not found.</returns>
    IModule? GetActiveModuleInstance(Guid moduleId);

    /// <summary>
    ///     Gets the active instance of a module by its instance ID (from preset configuration).
    ///     Returns null if the session is not running or the module is not active.
    /// </summary>
    /// <param name="instanceId">The unique identifier of the configured module instance.</param>
    /// <returns>The active module instance, or null if not found.</returns>
    IModule? GetActiveModuleInstanceByInstanceId(Guid instanceId);
}