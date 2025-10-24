namespace Axorith.Sdk.Http;

/// <summary>
/// Defines a contract for a factory that creates instances of <see cref="IHttpClient"/>.
/// </summary>
public interface IHttpClientFactory
{
    /// <summary>
    /// Creates and configures an instance of <see cref="IHttpClient"/>.
    /// </summary>
    /// <param name="name">A logical name for the client. Used by the Core for configuration and pooling.</param>
    /// <returns>A new instance of <see cref="IHttpClient"/>.</returns>
    IHttpClient CreateClient(string name);
}