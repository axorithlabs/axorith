using System.Net;
using System.Text;
using System.Text.Json;
using Axorith.Sdk;
using Axorith.Sdk.Http;
using Axorith.Sdk.Logging;

namespace Axorith.Module.Spotify;

/// <summary>
///     Service for communicating with Spotify Web API.
///     Handles authentication, retries, and rate limiting.
/// </summary>
internal sealed class SpotifyApiService(
    IHttpClientFactory httpClientFactory,
    ModuleDefinition definition,
    AuthService authService,
    IModuleLogger logger)
{
    private readonly IHttpClient _apiClient = httpClientFactory.CreateClient($"{definition.Name}.Api");

    private const int MaxRetries = 3;
    private const int BaseDelayMs = 500;
    private const int MaxJitterMs = 100;
    private const int VolumeMin = 0;
    private const int VolumeMax = 100;

    private async Task<bool> PrepareHttpClient()
    {
        var accessToken = await authService.GetValidAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogWarning("Cannot perform API call without a valid access token.");
            return false;
        }

        _apiClient.AddDefaultHeader("Authorization", $"Bearer {accessToken}");
        return true;
    }

    /// <summary>
    ///     Calculates retry delay with exponential backoff and jitter.
    /// </summary>
    private static TimeSpan GetRetryDelay(int attempt)
    {
        var baseDelay = Math.Pow(2, attempt) * BaseDelayMs;
        var jitter = Random.Shared.Next(0, MaxJitterMs);
        return TimeSpan.FromMilliseconds(baseDelay + jitter);
    }

    private async Task<T?> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName) where T : class
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                var statusCode = ex.StatusCode;

                // Retry on rate limit (429) or server errors (5xx)
                if (statusCode == HttpStatusCode.TooManyRequests ||
                    (statusCode.HasValue && (int)statusCode >= 500))
                {
                    var delay = GetRetryDelay(attempt);
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

    public async Task<List<SpotifyDevice>> GetDevicesAsync()
    {
        if (!await PrepareHttpClient())
        {
            return [];
        }

        var result = await ExecuteWithRetryAsync(async () =>
        {
            var responseJson = await _apiClient.GetStringAsync("https://api.spotify.com/v1/me/player/devices");
            using var jsonDoc = JsonDocument.Parse(responseJson);

            return jsonDoc.RootElement.GetProperty("devices").EnumerateArray().Select(element =>
                new SpotifyDevice(element.GetProperty("id").GetString() ?? string.Empty,
                    element.GetProperty("name").GetString() ?? "Unknown Device",
                    element.GetProperty("type").GetString() ?? "Unknown",
                    element.GetProperty("is_active").GetBoolean())).ToList();
        }, "GetDevices");

        return result ?? [];
    }

    public async Task<List<KeyValuePair<string, string>>> GetPlaylistsAsync()
    {
        if (!await PrepareHttpClient())
        {
            return [];
        }

        var result = await ExecuteWithRetryAsync(async () =>
        {
            var responseJson = await _apiClient.GetStringAsync("https://api.spotify.com/v1/me/playlists?limit=50");
            using var jsonDoc = JsonDocument.Parse(responseJson);

            if (!jsonDoc.RootElement.TryGetProperty("items", out var itemsElement))
            {
                return new List<KeyValuePair<string, string>>();
            }

            return
            [
                .. itemsElement.EnumerateArray()
                    .Select(p => new KeyValuePair<string, string>(
                        p.GetProperty("uri").GetString() ?? string.Empty,
                        $"{p.GetProperty("name").GetString() ?? "Unknown"} (Playlist)"))
            ];
        }, "GetPlaylists");

        return result ?? [];
    }

    public async Task<List<KeyValuePair<string, string>>> GetSavedAlbumsAsync()
    {
        if (!await PrepareHttpClient())
        {
            return [];
        }

        var result = await ExecuteWithRetryAsync<List<KeyValuePair<string, string>>>(async () =>
        {
            var responseJson = await _apiClient.GetStringAsync("https://api.spotify.com/v1/me/albums?limit=50");
            using var jsonDoc = JsonDocument.Parse(responseJson);
            return
            [
                .. jsonDoc.RootElement.GetProperty("items").EnumerateArray()
                    .Select(a => new KeyValuePair<string, string>(
                        a.GetProperty("album").GetProperty("uri").GetString() ?? string.Empty,
                        $"{a.GetProperty("album").GetProperty("name").GetString()} (Album)"))
            ];
        }, "GetSavedAlbums");

        return result ?? [];
    }

    public async Task<string> GetLikedSongsAsUriListAsync()
    {
        if (!await PrepareHttpClient())
        {
            return string.Empty;
        }

        var result = await ExecuteWithRetryAsync(async () =>
        {
            var responseJson = await _apiClient.GetStringAsync("https://api.spotify.com/v1/me/tracks?limit=50");
            using var jsonDoc = JsonDocument.Parse(responseJson);
            var tracks = jsonDoc.RootElement.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("track"));
            return JsonSerializer.Serialize(new { uris = tracks.Select(t => t.GetProperty("uri").GetString()) });
        }, "GetLikedSongs");

        return result ?? string.Empty;
    }

    public Task PlayAsync(string deviceId, string contextUri, IEnumerable<string>? trackUris = null)
    {
        var jsonContent = trackUris != null
            ? JsonSerializer.Serialize(new { uris = trackUris })
            : JsonSerializer.Serialize(new { context_uri = contextUri });

        return PutWithTokenAsync($"https://api.spotify.com/v1/me/player/play?device_id={deviceId}", jsonContent);
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

    public Task SetVolumeAsync(string deviceId, int volume)
    {
        volume = Math.Clamp(volume, VolumeMin, VolumeMax);
        return PutWithTokenAsync(
            $"https://api.spotify.com/v1/me/player/volume?volume_percent={volume}&device_id={deviceId}");
    }

    public Task SetShuffleAsync(string deviceId, bool shuffle)
    {
        return PutWithTokenAsync(
            $"https://api.spotify.com/v1/me/player/shuffle?state={shuffle.ToString().ToLowerInvariant()}&device_id={deviceId}");
    }

    public Task SetRepeatModeAsync(string deviceId, string repeatMode)
    {
        return PutWithTokenAsync(
            $"https://api.spotify.com/v1/me/player/repeat?state={repeatMode}&device_id={deviceId}");
    }

    private async Task PutWithTokenAsync(string uri, string? jsonContent = null)
    {
        if (!await PrepareHttpClient())
        {
            return;
        }

        if (jsonContent != null)
        {
            await _apiClient.PutStringAsync(uri, jsonContent, Encoding.UTF8, "application/json");
        }
        else
        {
            await _apiClient.PutAsync(uri);
        }
    }
}

/// <summary>
///     Represents a Spotify playback device.
/// </summary>
/// <param name="Id">Unique device identifier.</param>
/// <param name="Name">Human-readable device name.</param>
/// <param name="Type">Device type (Computer, Smartphone, Speaker, etc.).</param>
/// <param name="IsActive">Whether this device is currently active.</param>
public record SpotifyDevice(string Id, string Name, string Type, bool IsActive);