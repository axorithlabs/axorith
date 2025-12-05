namespace Axorith.Client.Services.Abstractions;

public interface IHostController
{
    Task<bool> IsHostReachableAsync(CancellationToken ct = default);
    Task StartHostAsync(bool forceRestart = false, CancellationToken ct = default);
    Task StopHostAsync(CancellationToken ct = default);
    Task RestartHostAsync(CancellationToken ct = default);
}