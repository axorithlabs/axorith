using System.Net;
using System.Text;
using System.Text.Json;
using Axorith.Sdk.Logging;

namespace Axorith.Module.ApplicationLauncher.Apps.Spotify;

internal sealed class SpotifyApiService(
    IHttpClientFactory httpClientFactory,
    AuthService authService,
    IModuleLogger logger)
{
    private readonly HttpClient _apiClient = httpClientFactory.CreateClient("Spotify.Api");

    private const int MaxRetries = 3;
    private const int BaseDelayMs = 500;
    private const int MaxJitterMs = 100;
    private const int VolumeMin = 0;
    private const int VolumeMax = 100;

    private async Task<string?> GetAccessTokenForRequestAsync()
    {
        var accessToken = await authService.GetValidAccessTokenAsync();
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            return accessToken;
        }

        logger.LogWarning("Cannot perform API call without a valid access token.");
        return null;
    }

    private async Task<T?> ExecuteWithRetryAsync<T>(Func<string, Task<T>> operation, string operationName)
        where T : class
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var token = await GetAccessTokenForRequestAsync();
            if (token == null)
            {
                return null;
            }

            try
            {
                return await operation(token);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                var statusCode = ex.StatusCode;

                if (statusCode == HttpStatusCode.TooManyRequests ||
                    (statusCode.HasValue && (int)statusCode >= 500))
                {
                    var delay = TimeSpan.FromMilliseconds(
                        Math.Pow(2, attempt) * BaseDelayMs + Random.Shared.Next(MaxJitterMs));
                    logger.LogWarning(
                        "Spotify API {Operation} failed with {StatusCode}, retrying in {Delay}ms (attempt {Attempt}/{MaxRetries})",
                        operationName, statusCode, delay.TotalMilliseconds, attempt + 1, MaxRetries);
                    await Task.Delay(delay);
                    continue;
                }

                throw;
            }
        }

        return null;
    }

    private Task<T?> GetJsonAsync<T>(string uri, string operationName, Func<JsonElement, T> map)
        where T : class =>
        ExecuteWithRetryAsync(async token =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("Authorization", $"Bearer {token}");
            using var response = await _apiClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return map(document.RootElement);
        }, operationName);

    public async Task<List<SpotifyDevice>> GetDevicesAsync() =>
        await GetJsonAsync("https://api.spotify.com/v1/me/player/devices", "GetDevices", root =>
            root.GetProperty("devices").EnumerateArray().Select(element =>
                new SpotifyDevice(
                    element.GetProperty("id").GetString() ?? string.Empty,
                    element.GetProperty("name").GetString() ?? "Unknown Device",
                    element.GetProperty("type").GetString() ?? "Unknown",
                    element.GetProperty("is_active").GetBoolean())).ToList()) ?? [];

    public async Task<List<KeyValuePair<string, string>>> GetPlaylistsAsync() =>
        await GetJsonAsync("https://api.spotify.com/v1/me/playlists?limit=50", "GetPlaylists", root =>
        {
            if (!root.TryGetProperty("items", out var items))
            {
                return [];
            }

            return items.EnumerateArray()
                .Select(item => new KeyValuePair<string, string>(
                    item.GetProperty("uri").GetString() ?? string.Empty,
                    $"{item.GetProperty("name").GetString() ?? "Unknown"} (Playlist)"))
                .ToList();
        }) ?? [];

    public async Task<List<KeyValuePair<string, string>>> GetSavedAlbumsAsync() =>
        await GetJsonAsync("https://api.spotify.com/v1/me/albums?limit=50", "GetSavedAlbums", root =>
            root.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("album"))
                .Select(album => new KeyValuePair<string, string>(
                    album.GetProperty("uri").GetString() ?? string.Empty,
                    $"{album.GetProperty("name").GetString()} (Album)"))
                .ToList()) ?? [];

    public async Task<string> GetLikedSongsAsUriListAsync() =>
        await GetJsonAsync("https://api.spotify.com/v1/me/tracks?limit=50", "GetLikedSongs", root =>
        {
            var tracks = root.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("track"));
            return JsonSerializer.Serialize(new { uris = tracks.Select(track => track.GetProperty("uri").GetString()) });
        }) ?? string.Empty;

    public async Task PlayAsync(string deviceId, string contextUri, IEnumerable<string>? trackUris = null)
    {
        var jsonContent = trackUris != null
            ? JsonSerializer.Serialize(new { uris = trackUris })
            : JsonSerializer.Serialize(new { context_uri = contextUri });

        await PutWithTokenAsync($"https://api.spotify.com/v1/me/player/play?device_id={deviceId}", jsonContent);
    }

    public async Task PauseAsync()
    {
        try
        {
            await PutWithTokenAsync("https://api.spotify.com/v1/me/player/pause");
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                logger.LogDebug("Pause request ignored: Spotify reported no active playback or device.");
            }
            else
            {
                throw;
            }
        }
    }

    public async Task SetVolumeAsync(string deviceId, int volume)
    {
        volume = Math.Clamp(volume, VolumeMin, VolumeMax);
        await PutWithTokenAsync(
            $"https://api.spotify.com/v1/me/player/volume?volume_percent={volume}&device_id={deviceId}");
    }

    public async Task SetShuffleAsync(string deviceId, bool shuffle)
    {
        await PutWithTokenAsync(
            $"https://api.spotify.com/v1/me/player/shuffle?state={shuffle.ToString().ToLowerInvariant()}&device_id={deviceId}");
    }

    public async Task SetRepeatModeAsync(string deviceId, string repeatMode)
    {
        await PutWithTokenAsync(
            $"https://api.spotify.com/v1/me/player/repeat?state={repeatMode}&device_id={deviceId}");
    }

    private async Task PutWithTokenAsync(string uri, string? jsonContent = null)
    {
        var token = await GetAccessTokenForRequestAsync();
        if (token == null)
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, uri);
        request.Headers.Add("Authorization", $"Bearer {token}");

        if (jsonContent != null)
        {
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        }

        using var response = await _apiClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}

public record SpotifyDevice(string Id, string Name, string Type, bool IsActive);
