using System.Text;
using Axorith.Sdk.Http;

namespace Axorith.Core.Http;

/// <summary>
/// Adapter that wraps a real HttpClient and exposes it as IHttpClient.
/// </summary>
internal class HttpClientAdapter(HttpClient httpClient) : IHttpClient
{
    public void AddDefaultHeader(string name, string value)
    {
        httpClient.DefaultRequestHeaders.Add(name, value);
    }

    public Task<string> GetStringAsync(string requestUri, CancellationToken cancellationToken = default)
    {
        return httpClient.GetStringAsync(requestUri, cancellationToken);
    }

    public async Task<string> PostStringAsync(string requestUri, string content, Encoding encoding, string mediaType, CancellationToken cancellationToken = default)
    {
        using var stringContent = new StringContent(content, encoding, mediaType);
        using var response = await httpClient.PostAsync(requestUri, stringContent, cancellationToken);
        
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}