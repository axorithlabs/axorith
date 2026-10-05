using Axorith.Core.Models;

namespace Axorith.Core.Services.Abstractions;

public interface ISessionAutoStopService : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StartTrackingAsync(Guid sessionInstanceId, TimeSpan? autoStopDuration, Guid? nextPresetId,
        CancellationToken cancellationToken = default, SessionSchedule? schedule = null);

    Task StopTrackingAsync(CancellationToken cancellationToken = default);

    TimeSpan? GetTimeRemaining();

    Task ExecuteAfterEndActionAsync(Axorith.Core.Models.AfterEndBehavior behavior);

    Task<bool> CompleteNaturallyAsync(Axorith.Core.Models.SessionPreset expectedSession,
        Guid? fallbackNextPresetId, CancellationToken cancellationToken = default, SessionSchedule? schedule = null);
}
