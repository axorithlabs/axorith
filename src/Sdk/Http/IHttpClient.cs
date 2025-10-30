using System.Text;

namespace Axorith.Sdk.Http;

/// <summary>
///     Defines a fully abstracted contract for an HTTP client usable by modules.
///     It does not expose any types from System.Net.Http.
/// </summary>
public interface IHttpClient
{
    /// <summary>
    ///     Adds a header to be sent with every request from this client.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value.</param>
    void AddDefaultHeader(string name, string value);

    /// <summary>
    ///     Sends a GET request and returns the response body as a string.
    /// </summary>
    Task<string> GetStringAsync(string requestUri, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Sends a POST request with a string body.
    /// </summary>
    /// <param name="requestUri">The Uri the request is sent to.</param>
    /// <param name="content">The string content to send.</param>
    /// <param name="encoding">The encoding to use for the content.</param>
    /// <param name="mediaType">The media type of the content (e.g., "application/json").</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The response body as a string.</returns>
    Task<string> PostStringAsync(string requestUri, string content, Encoding encoding, string mediaType,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Sends a POST request with an empty body.
    /// </summary>
    /// <param name="requestUri">The Uri the request is sent to.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The response body as a string.</returns>
    Task<string> PostStringAsync(string requestUri, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Sends a PUT request with a string body.
    /// </summary>
    /// <param name="requestUri">The Uri the request is sent to.</param>
    /// <param name="content">The string content to send.</param>
    /// <param name="encoding">The encoding to use for the content.</param>
    /// <param name="mediaType">The media type of the content (e.g., "application/json").</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The response body as a string.</returns>
    Task<string> PutStringAsync(string requestUri, string content, Encoding encoding, string mediaType,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Sends a PUT request with an empty body.
    /// </summary>
    /// <param name="requestUri">The Uri the request is sent to.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The response body as a string.</returns>
    Task<string> PutAsync(string requestUri, CancellationToken cancellationToken = default);
}