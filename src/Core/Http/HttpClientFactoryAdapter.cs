using Axorith.Sdk.Http;
using IHttpClientFactory = Axorith.Sdk.Http.IHttpClientFactory;

namespace Axorith.Core.Http;

/// <summary>
///     The CORRECT implementation of our factory. It USES the built-in IHttpClientFactory
///     to properly manage handler lifetimes and prevent DNS issues.
/// </summary>
public class HttpClientFactoryAdapter(System.Net.Http.IHttpClientFactory realFactory) : IHttpClientFactory
{
    public IHttpClient CreateClient(string name)
    {
        // Always use "default" client to ensure Polly policies are applied
        // The name parameter is used only for User-Agent customization
        var realHttpClient = realFactory.CreateClient("default");

        realHttpClient.DefaultRequestHeaders.Add("User-Agent", $"Axorith/{name}");

        return new HttpClientAdapter(realHttpClient);
    }
}