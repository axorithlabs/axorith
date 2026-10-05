using Axorith.Core.Models;
using Axorith.Sdk;

namespace Axorith.Core.Services.Abstractions;

public interface ISessionManager : IAsyncDisposable
{
    bool IsSessionRunning { get; }

    SessionPreset? ActiveSession { get; }

    Guid? CurrentSessionInstanceId { get; }

    DateTimeOffset? SessionStartedAt { get; }

    DateTimeOffset? SessionEndsAt { get; }

    TimeSpan? SessionTimeRemaining { get; }

    DateTimeOffset? BreakEndsAt { get; }

    int BreaksRemaining { get; }

    TimeSpan? BreakTimeRemaining { get; }

    string ProtectionStatus { get; }

    IReadOnlyList<SessionActivity> SessionHistory { get; }

    SessionSnapshot? GetCurrentSnapshot();

    event Action<Guid>? SessionStarted;

    event Action<Guid>? SessionStopped;

    Task StartSessionAsync(SessionPreset preset, CancellationToken cancellationToken = default,
        string startSource = "manual", Guid? sessionInstanceId = null, Guid? scheduleId = null,
        Guid? previousSessionInstanceId = null);

    Task PreflightSessionAsync(SessionPreset preset, CancellationToken cancellationToken = default);

    Task RecoverCommittedSessionAsync(CancellationToken cancellationToken = default);

    Task StopCurrentSessionAsync(CancellationToken cancellationToken = default);

    Task<bool> EndCommittedSessionAsync(SessionEndReason reason, CancellationToken cancellationToken = default);

    Task StartBreakAsync(CancellationToken cancellationToken = default);

    Task EndBreakAsync(CancellationToken cancellationToken = default);

    Task RefreshProtectionHealthAsync(CancellationToken cancellationToken = default);

    IModule? GetActiveModuleInstanceByInstanceId(Guid instanceId);
}
