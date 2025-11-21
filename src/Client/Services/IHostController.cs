namespace Axorith.Client.Services;

public interface IHostController
{
    Task<bool> IsHostReachableAsync(CancellationToken ct = default);
    Task StartHostAsync(CancellationToken ct = default);
    Task StopHostAsync(CancellationToken ct = default);
    Task RestartHostAsync(CancellationToken ct = default);
}