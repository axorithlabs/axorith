using System.Net;
using System.Text;
using System.Text.Json;
using Axorith.Sdk;
using Axorith.Sdk.Http;
using Axorith.Sdk.Logging;

namespace Axorith.Module.SpotifyPlayer;

internal sealed class SpotifyApiService(
    IHttpClientFactory httpClientFactory,
    ModuleDefinition definition,
    AuthService authService,
    IModuleLogger logger)
{
    private readonly IHttpClient _apiClient = httpClientFactory.CreateClient($"{definition.Name}.Api");

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

    public async Task<List<KeyValuePair<string, string>>> GetAvailableDevicesAsync()
    {
        if (!await PrepareHttpClient())
        {
            return [];
        }

        var responseJson = await _apiClient.GetStringAsync("https://api.spotify.com/v1/me/player/devices");
        using var jsonDoc = JsonDocument.Parse(responseJson);
        return
        [
            .. jsonDoc.RootElement.GetProperty("devices").EnumerateArray()
                .Select(d => new KeyValuePair<string, string>(
                    d.GetProperty("id").GetString() ?? string.Empty,
                    $"{d.GetProperty("name").GetString()} ({d.GetProperty("type").GetString()})"))
        ];
    }

    public async Task<List<KeyValuePair<string, string>>> GetPlaylistsAsync()
    {
        if (!await PrepareHttpClient())
        {
            return [];
        }

        var responseJson = await _apiClient.GetStringAsync("https://api.spotify.com/v1/me/playlists");
        using var jsonDoc = JsonDocument.Parse(responseJson);
        return
        [
            .. jsonDoc.RootElement.GetProperty("items").EnumerateArray()
                .Select(p => new KeyValuePair<string, string>(
                    p.GetProperty("uri").GetString() ?? string.Empty,
                    $"{p.GetProperty("name").GetString()} (Playlist)"))
        ];
    }

    public async Task<List<KeyValuePair<string, string>>> GetSavedAlbumsAsync()
    {
        if (!await PrepareHttpClient())
        {
            return [];
        }

        var responseJson = await _apiClient.GetStringAsync("https://api.spotify.com/v1/me/albums");
        using var jsonDoc = JsonDocument.Parse(responseJson);
        return
        [
            .. jsonDoc.RootElement.GetProperty("items").EnumerateArray()
                .Select(a => new KeyValuePair<string, string>(
                    a.GetProperty("album").GetProperty("uri").GetString() ?? string.Empty,
                    $"{a.GetProperty("album").GetProperty("name").GetString()} (Album)"))
        ];
    }

    public async Task<string> GetLikedSongsAsUriListAsync()
    {
        if (!await PrepareHttpClient())
        {
            return string.Empty;
        }

        var responseJson = await _apiClient.GetStringAsync("https://api.spotify.com/v1/me/tracks?limit=50");
        using var jsonDoc = JsonDocument.Parse(responseJson);
        var tracks = jsonDoc.RootElement.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("track"));
        return JsonSerializer.Serialize(new { uris = tracks.Select(t => t.GetProperty("uri").GetString()) });
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
            if (ex.StatusCode == HttpStatusCode.NotFound ||
                ex.StatusCode == HttpStatusCode.Forbidden)
            {
                logger.LogDebug("Pause request ignored: Spotify reported no active playback or device.");
            }
            else
            {
                throw; // Rethrow other errors (e.g. 401 Unauthorized, 500 Server Error)
            }
        }
    }

    public Task SetVolumeAsync(string deviceId, int volume)
    {
        volume = Math.Clamp(volume, 0, 100);
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