namespace Axorith.Core.Services.Abstractions;

public interface ICommitmentProtectionService
{
    Task CheckCanEnableAsync(CancellationToken cancellationToken = default);
    Task CheckRecoveryStateAsync(CancellationToken cancellationToken = default);
    Task CheckCanSetRecoveryStartupAsync(CancellationToken cancellationToken = default);
    Task SetRecoveryStartupAsync(bool active, CancellationToken cancellationToken = default);
    Task EnableAsync(CancellationToken cancellationToken = default);
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);
    Task RestoreAsync(CancellationToken cancellationToken = default);
    Task ReconcileAsync(bool strictSessionActive, CancellationToken cancellationToken = default);
}
