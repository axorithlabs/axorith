using Axorith.Sdk.Http;
using IHttpClientFactory = Axorith.Sdk.Http.IHttpClientFactory;

namespace Axorith.Core.Http;

/// <summary>
///     Adapter that wraps the standard HttpClientFactory and exposes it as IHttpClientFactory (SDK interface).
///     Creates isolated HTTP clients per module to ensure circuit breaker isolation.
/// </summary>
public class HttpClientFactoryAdapter(System.Net.Http.IHttpClientFactory realFactory) : IHttpClientFactory
{
    public IHttpClient CreateClient(string name)
    {
        var clientName = string.IsNullOrWhiteSpace(name) ? "default" : $"module-{name}";

        var realHttpClient = realFactory.CreateClient(clientName);
        realHttpClient.DefaultRequestHeaders.Add("User-Agent", $"Axorith/{name}");

        return new HttpClientAdapter(realHttpClient);
    }
}