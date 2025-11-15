namespace Axorith.Client.CoreSdk;

/// <summary>
///     API for session management and event streaming.
/// </summary>
public interface ISessionsApi
{
    /// <summary>
    ///     Gets current session state.
    ///     Returns null if no session is active.
    /// </summary>
    Task<SessionState?> GetCurrentSessionAsync(CancellationToken ct = default);

    /// <summary>
    ///     Starts a session from a preset.
    ///     Validates all modules before starting.
    /// </summary>
    Task<OperationResult> StartSessionAsync(Guid presetId, CancellationToken ct = default);

    /// <summary>
    ///     Stops the currently active session.
    /// </summary>
    Task<OperationResult> StopSessionAsync(CancellationToken ct = default);

    /// <summary>
    ///     Observable stream of session events (started, stopped, module events).
    ///     Automatically reconnects on connection loss.
    /// </summary>
    IObservable<SessionEvent> SessionEvents { get; }
}

/// <summary>
///     Current session state information.
/// </summary>
public record SessionState(
    bool IsActive,
    Guid? PresetId,
    string? PresetName,
    DateTimeOffset? StartedAt
);

/// <summary>
///     Session event notification.
/// </summary>
public record SessionEvent(
    SessionEventType Type,
    Guid? PresetId,
    string? Message,
    DateTimeOffset Timestamp
);

/// <summary>
///     Types of session events.
/// </summary>
public enum SessionEventType
{
    /// <summary>Session started successfully.</summary>
    Started,

    /// <summary>Session stopped.</summary>
    Stopped,

    /// <summary>A module started within the session.</summary>
    ModuleStarted,

    /// <summary>A module stopped within the session.</summary>
    ModuleStopped,

    /// <summary>A module encountered an error.</summary>
    ModuleError,

    /// <summary>A validation warning occurred.</summary>
    ValidationWarning
}

/// <summary>
///     Result of an operation with success/failure and optional messages.
/// </summary>
public record OperationResult(
    bool Success,
    string Message,
    IReadOnlyList<string>? Errors = null,
    IReadOnlyList<string>? Warnings = null
);