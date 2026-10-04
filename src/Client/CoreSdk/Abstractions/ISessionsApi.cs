using Axorith.Core.Models;

namespace Axorith.Client.CoreSdk.Abstractions;

public interface ISessionsApi
{
    Task<SessionState?> GetCurrentSessionAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SessionActivity>> GetSessionHistoryAsync(CancellationToken ct = default);

    Task<OperationResult> PreflightSessionAsync(Guid presetId, CancellationToken ct = default);

    Task<OperationResult> StartSessionAsync(Guid presetId, Guid sessionInstanceId, CancellationToken ct = default);

    Task<OperationResult> StopSessionAsync(CancellationToken ct = default);

    Task<OperationResult> StartBreakAsync(CancellationToken ct = default);

    IAsyncEnumerable<EmergencyUnlockProgress> HoldEmergencyUnlockAsync(IAsyncEnumerable<bool> heldSignals,
        CancellationToken ct = default);

    IObservable<SessionEvent> SessionEvents { get; }
}

public record SessionState(
    bool IsActive,
    Guid? PresetId,
    string? PresetName,
    DateTimeOffset? StartedAt,
    FocusCommitmentMode FocusCommitment = FocusCommitmentMode.Normal,
    DateTimeOffset? EndsAt = null,
    int BreaksRemaining = 0,
    AfterEndBehavior AfterEnd = AfterEndBehavior.DoNothing,
    string ProtectionStatus = "inactive",
    bool EmergencyUnlockAvailable = false,
    DateTimeOffset? BreakEndsAt = null,
    bool AppBlocking = false,
    bool WebsiteBlocking = false,
    int BreaksTotal = 0,
    TimeSpan? Remaining = null,
    TimeSpan? BreakRemaining = null
);

public record EmergencyUnlockProgress(double Progress, bool Completed, string Message);

public record SessionEvent(
    SessionEventType Type,
    Guid? PresetId,
    string? Message,
    DateTimeOffset Timestamp
);

public enum SessionEventType
{
    Started,

    Stopped,

    ModuleStarted,

    ModuleStopped,

    ModuleError,

    ValidationWarning
}

public record OperationResult(
    bool Success,
    string Message,
    IReadOnlyList<string>? Errors = null,
    IReadOnlyList<string>? Warnings = null
);
