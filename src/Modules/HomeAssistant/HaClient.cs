using System.Text;
using System.Text.Json;
using Axorith.Sdk.Logging;

namespace Axorith.Module.HomeAssistant;

internal class HaClient(IHttpClientFactory clientFactory, IModuleLogger logger)
{
    private readonly HttpClient _client = clientFactory.CreateClient("HomeAssistant");

    public async Task CallServiceAsync(string baseUrl, string token, string domain, string service, string entityId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(entityId))
        {
            return;
        }

        baseUrl = baseUrl.TrimEnd('/');
        if (!baseUrl.StartsWith("http"))
        {
            baseUrl = $"http://{baseUrl}";
        }

        var url = $"{baseUrl}/api/services/{domain}/{service}";

        logger.LogDebug("Calling HA Service: {Url} for Entity: {Entity}", url, entityId);

        var payload = new { entity_id = entityId };
        var json = JsonSerializer.Serialize(payload);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {token}");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            logger.LogInfo("Successfully called {Domain}.{Service} for {Entity}", domain, service, entityId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to call Home Assistant API at {Url}", url);
            throw;
        }
    }

    public async Task<bool> CheckConnectionAsync(string baseUrl, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        baseUrl = baseUrl.TrimEnd('/');
        if (!baseUrl.StartsWith("http"))
        {
            baseUrl = $"http://{baseUrl}";
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/");
            request.Headers.Add("Authorization", $"Bearer {token}");

            using var response = await _client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch
        {
            return false;
        }
    }
}