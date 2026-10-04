using System.Net;
using System.Text.Json;
using Axorith.Sdk.Logging;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace Axorith.Module.ApplicationLauncher.Apps.Spotify;

internal sealed class SpotifyApiService(
    IHttpClientFactory httpClientFactory,
    AuthService authService,
    IModuleLogger logger)
{
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 500;
    private const int VolumeMin = 0;
    private const int VolumeMax = 100;

    private readonly NetHttpClient _httpClient = new(httpClientFactory.CreateClient("Spotify.Api"));
    private readonly SimpleRetryHandler _retryHandler = new()
    {
        RetryTimes = MaxRetries,
        RetryAfter = TimeSpan.FromMilliseconds(RetryDelayMs),
        TooManyRequestsConsumesARetry = true,
        RetryErrorCodes = [HttpStatusCode.InternalServerError, HttpStatusCode.BadGateway,
            HttpStatusCode.ServiceUnavailable, HttpStatusCode.GatewayTimeout]
    };

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

    private async Task<T?> ExecuteAsync<T>(Func<SpotifyClient, Task<T>> operation)
    {
        var token = await GetAccessTokenForRequestAsync().ConfigureAwait(false);
        if (token is null)
        {
            return default;
        }

        var config = SpotifyClientConfig.CreateDefault(token)
            .WithHTTPClient(_httpClient)
            .WithRetryHandler(_retryHandler);
        return await operation(new SpotifyClient(config)).ConfigureAwait(false);
    }

    public async Task<List<SpotifyDevice>> GetDevicesAsync()
    {
        var response = await ExecuteAsync(client => client.Player.GetAvailableDevices()).ConfigureAwait(false);
        return response?.Devices.Select(device => new SpotifyDevice(
            device.Id,
            device.Name,
            device.Type,
            device.IsActive)).ToList() ?? [];
    }

    public async Task<List<KeyValuePair<string, string>>> GetPlaylistsAsync()
    {
        var response = await ExecuteAsync(client => client.Playlists.CurrentUsers(
            new PlaylistCurrentUsersRequest { Limit = 50 })).ConfigureAwait(false);
        return response?.Items?.Select(playlist => KeyValuePair.Create(
            playlist.Uri ?? string.Empty,
            $"{playlist.Name ?? "Unknown"} (Playlist)")).ToList() ?? [];
    }

    public async Task<List<KeyValuePair<string, string>>> GetSavedAlbumsAsync()
    {
        var response = await ExecuteAsync(client => client.Library.GetAlbums(
            new LibraryAlbumsRequest { Limit = 50 })).ConfigureAwait(false);
        return response?.Items?.Select(saved => KeyValuePair.Create(
            saved.Album.Uri ?? string.Empty,
            $"{saved.Album.Name ?? "Unknown"} (Album)")).ToList() ?? [];
    }

    public async Task<string> GetLikedSongsAsUriListAsync()
    {
        var response = await ExecuteAsync(client => client.Library.GetTracks(
            new LibraryTracksRequest { Limit = 50 })).ConfigureAwait(false);
        if (response is null)
        {
            return string.Empty;
        }

        var uris = response.Items?.Select(saved => saved.Track.Uri).ToList() ?? [];
        return JsonSerializer.Serialize(new { uris });
    }

    public async Task PlayAsync(string deviceId, string contextUri, IEnumerable<string>? trackUris = null)
    {
        var request = new PlayerResumePlaybackRequest { DeviceId = deviceId };
        if (trackUris is null)
        {
            request.ContextUri = contextUri;
        }
        else
        {
            request.Uris = trackUris.ToList();
        }

        await ExecuteAsync(client => client.Player.ResumePlayback(request)).ConfigureAwait(false);
    }

    public async Task PauseAsync()
    {
        try
        {
            await ExecuteAsync(client => client.Player.PausePlayback()).ConfigureAwait(false);
        }
        catch (APIException ex) when (ex.Response?.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            logger.LogDebug("Pause request ignored: Spotify reported no active playback or device.");
        }
    }

    public async Task SetVolumeAsync(string deviceId, int volume)
    {
        var request = new PlayerVolumeRequest(Math.Clamp(volume, VolumeMin, VolumeMax)) { DeviceId = deviceId };
        await ExecuteAsync(client => client.Player.SetVolume(request)).ConfigureAwait(false);
    }

    public async Task SetShuffleAsync(string deviceId, bool shuffle)
    {
        var request = new PlayerShuffleRequest(shuffle) { DeviceId = deviceId };
        await ExecuteAsync(client => client.Player.SetShuffle(request)).ConfigureAwait(false);
    }

    public async Task SetRepeatModeAsync(string deviceId, string repeatMode)
    {
        var state = repeatMode.ToLowerInvariant() switch
        {
            "track" => PlayerSetRepeatRequest.State.Track,
            "context" => PlayerSetRepeatRequest.State.Context,
            _ => PlayerSetRepeatRequest.State.Off
        };
        var request = new PlayerSetRepeatRequest(state) { DeviceId = deviceId };
        await ExecuteAsync(client => client.Player.SetRepeat(request)).ConfigureAwait(false);
    }
}

public record SpotifyDevice(string Id, string Name, string Type, bool IsActive);
