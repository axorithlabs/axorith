namespace Axorith.Client.Services.Abstractions;

public interface ITokenProvider
{
    Task<string?> GetTokenAsync(CancellationToken ct = default);
}