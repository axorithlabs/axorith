namespace Axorith.Client.Services;

public interface ITokenProvider
{
    Task<string?> GetTokenAsync(CancellationToken ct = default);
}