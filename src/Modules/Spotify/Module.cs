using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Axorith.Sdk;
using Axorith.Sdk.Http;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;

namespace Axorith.Module.Spotify;

/// <summary>
///     A module to control Spotify playback, with fully automated authentication and playback options.
/// </summary>
public class Module(
    IModuleLogger logger,
    IHttpClientFactory httpClientFactory,
    ISecureStorageService secureStorage,
    ModuleDefinition definition) : IModule
{
    private readonly IHttpClient _apiClient = httpClientFactory.CreateClient($"{definition.Name}.Api");
    private readonly IHttpClient _authClient = httpClientFactory.CreateClient($"{definition.Name}.Auth");
    private string? _inMemoryAccessToken;

    private const string ClientIdKey = "SpotifyClientId";
    private const string ClientSecretKey = "SpotifyClientSecret";
    private const string RefreshTokenKey = "SpotifyRefreshToken";

    /// <inheritdoc />
    public IReadOnlyList<SettingBase> GetSettings()
    {
        return new List<SettingBase>
        {
            new SecretSetting(ClientIdKey, "Spotify Client ID", "Your Client ID from the Spotify Developer Dashboard."),
            new SecretSetting(ClientSecretKey, "Spotify Client Secret",
                "Your Client Secret from the Spotify Developer Dashboard."),
            new TextSetting("PlaylistUrl", "Spotify URL", "URL of the track, playlist, or album to play."),
            new NumberSetting("Volume", "Volume", "Playback volume (0-100).", 80),
            new ChoiceSetting("Shuffle", "Shuffle Mode", new[]
            {
                new KeyValuePair<string, string>("true", "On"),
                new KeyValuePair<string, string>("false", "Off")
            }, "false"),
            new ChoiceSetting("RepeatMode", "Repeat Mode", new[]
            {
                new KeyValuePair<string, string>("off", "Off"),
                new KeyValuePair<string, string>("context", "Repeat Playlist/Album"),
                new KeyValuePair<string, string>("track", "Repeat Track")
            }, "off")
        };
    }

    /// <inheritdoc />
    public Type? CustomSettingsViewType => null;

    /// <inheritdoc />
    public object? GetSettingsViewModel(IReadOnlyDictionary<string, string> currentSettings)
    {
        return null;
    }

    /// <inheritdoc />
    public Task<ValidationResult> ValidateSettingsAsync(IReadOnlyDictionary<string, string> userSettings,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ValidationResult.Success);
    }

    /// <inheritdoc />
    public async Task OnSessionStartAsync(IReadOnlyDictionary<string, string> userSettings,
        CancellationToken cancellationToken)
    {
        var accessToken = await GetValidAccessTokenAsync(userSettings);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogError(null,
                "Could not obtain a valid Spotify Access Token. Please configure the module and log in via its settings.");
            return;
        }

        _apiClient.AddDefaultHeader("Authorization", $"Bearer {accessToken}");

        if (!await HasActiveDeviceAsync())
        {
            logger.LogError(null,
                "ERROR: No active Spotify devices found! Open Spotify on any device, play a song for a second, then try again.");
            return;
        }

        logger.LogInfo("Configuring playback options...");
        try
        {
            var volume = int.Parse(userSettings.GetValueOrDefault("Volume", "80"));
            var shuffle = bool.Parse(userSettings.GetValueOrDefault("Shuffle", "false"));
            var repeatMode = userSettings.GetValueOrDefault("RepeatMode", "off");

            var setupTasks = new List<Task>
            {
                SetVolumeAsync(volume),
                SetShuffleAsync(shuffle),
                SetRepeatModeAsync(repeatMode)
            };

            await Task.WhenAll(setupTasks);
            logger.LogInfo("Playback options configured successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "One or more playback options failed to apply, but continuing anyway.");
        }
        // ----------------------------------------------------

        var spotifyUrl = userSettings.GetValueOrDefault("PlaylistUrl", string.Empty);
        if (!string.IsNullOrWhiteSpace(spotifyUrl)) await StartPlaybackAsync(spotifyUrl, cancellationToken);
    }

    /// <inheritdoc />
    public async Task OnSessionEndAsync()
    {
        var accessToken = await GetValidAccessTokenAsync(new Dictionary<string, string>());
        if (string.IsNullOrWhiteSpace(accessToken)) return;

        _apiClient.AddDefaultHeader("Authorization", $"Bearer {accessToken}");
        await StopPlaybackAsync();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Gets a valid access token, refreshing it if necessary or performing initial login.
    /// </summary>
    private async Task<string?> GetValidAccessTokenAsync(IReadOnlyDictionary<string, string> userSettings)
    {
        if (!string.IsNullOrWhiteSpace(_inMemoryAccessToken)) return _inMemoryAccessToken;

        var refreshToken = secureStorage.RetrieveSecret(RefreshTokenKey);
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            logger.LogWarning("Refresh Token not found. Attempting to perform initial login...");
            refreshToken = await PerformInitialLoginAsync(userSettings);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                logger.LogError(null, "Initial login failed. Please check your Client ID/Secret and try again.");
                return null;
            }
        }

        logger.LogInfo("Access token expired or not found. Refreshing...");
        var clientId = userSettings.GetValueOrDefault(ClientIdKey, string.Empty);
        var clientSecret = userSettings.GetValueOrDefault(ClientSecretKey, string.Empty);

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            logger.LogError(null, "Client ID or Client Secret is not configured.");
            return null;
        }

        try
        {
            var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            _authClient.AddDefaultHeader("Authorization", $"Basic {authHeader}");
            var content = $"grant_type=refresh_token&refresh_token={refreshToken}";
            var responseJson = await _authClient.PostStringAsync("https://accounts.spotify.com/api/token", content,
                Encoding.UTF8, "application/x-www-form-urlencoded");
            using var jsonDoc = JsonDocument.Parse(responseJson);
            var newAccessToken = jsonDoc.RootElement.GetProperty("access_token").GetString();

            if (string.IsNullOrWhiteSpace(newAccessToken))
            {
                logger.LogError(null, "Failed to refresh token: API response did not contain an access_token.");
                return null;
            }

            logger.LogInfo("Successfully refreshed access token.");
            _inMemoryAccessToken = newAccessToken;
            return _inMemoryAccessToken;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to refresh token. The refresh token might be invalid. Attempting to re-login on next session start.");
            secureStorage.StoreSecret(RefreshTokenKey, string.Empty);
            return null;
        }
    }

    /// <summary>
    ///     Performs the initial OAuth2 login flow to get a refresh token.
    /// </summary>
    private async Task<string?> PerformInitialLoginAsync(IReadOnlyDictionary<string, string> userSettings)
    {
        var clientId = userSettings.GetValueOrDefault(ClientIdKey, string.Empty);
        var clientSecret = userSettings.GetValueOrDefault(ClientSecretKey, string.Empty);

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            logger.LogError(null,
                "Cannot start login process: Spotify Client ID or Client Secret is not set in the module settings.");
            return null;
        }

        secureStorage.StoreSecret(ClientIdKey, clientId);
        secureStorage.StoreSecret(ClientSecretKey, clientSecret);

        const string redirectUri = "http://127.0.0.1:8888/callback/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);

        try
        {
            listener.Start();
            var scopes = "user-modify-playback-state user-read-playback-state";
            var authUrl =
                $"https://accounts.spotify.com/authorize?client_id={clientId}&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString(scopes)}";

            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
            logger.LogWarning("Your browser has been opened to log in to Spotify. Please grant access.");

            var context = await listener.GetContextAsync();
            var code = context.Request.QueryString.Get("code");

            var response = context.Response;
            var buffer =
                "<html><body><h1>Success!</h1><p>You can now close this browser tab and return to Axorith.</p></body></html>"u8
                    .ToArray();
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer);
            response.OutputStream.Close();
            listener.Stop();

            if (string.IsNullOrWhiteSpace(code)) return null;

            var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            _authClient.AddDefaultHeader("Authorization", $"Basic {authHeader}");
            var content = $"grant_type=authorization_code&code={code}&redirect_uri={Uri.EscapeDataString(redirectUri)}";
            var responseJson = await _authClient.PostStringAsync("https://accounts.spotify.com/api/token", content,
                Encoding.UTF8, "application/x-www-form-urlencoded");

            using var jsonDoc = JsonDocument.Parse(responseJson);
            var refreshToken = jsonDoc.RootElement.GetProperty("refresh_token").GetString();

            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                secureStorage.StoreSecret(RefreshTokenKey, refreshToken);
                logger.LogInfo("SUCCESS: New Refresh Token has been obtained and saved securely.");
                return refreshToken;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed during initial login process.");
        }
        finally
        {
            if (listener.IsListening) listener.Stop();
        }

        return null;
    }

    /// <summary>
    ///     Checks if there is an active device to play music on.
    /// </summary>
    private async Task<bool> HasActiveDeviceAsync()
    {
        try
        {
            var responseJson = await _apiClient.GetStringAsync("https://api.spotify.com/v1/me/player/devices");
            using var jsonDoc = JsonDocument.Parse(responseJson);
            return jsonDoc.RootElement.GetProperty("devices").EnumerateArray()
                .Any(device => device.GetProperty("is_active").GetBoolean());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Converts a Spotify URL to a URI.
    /// </summary>
    private static string ConvertUrlToUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) throw new ArgumentException("Invalid URL format");
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2) throw new ArgumentException("URL does not contain a valid Spotify type and ID");
        return $"spotify:{segments[0]}:{segments[1]}";
    }

    /// <summary>
    ///     Starts playback of a given track, playlist, or album.
    /// </summary>
    private async Task StartPlaybackAsync(string spotifyUrl, CancellationToken cancellationToken)
    {
        try
        {
            var spotifyUri = ConvertUrlToUri(spotifyUrl);
            var jsonContent = spotifyUri.Contains(":track:")
                ? JsonSerializer.Serialize(new { uris = new[] { spotifyUri } })
                : JsonSerializer.Serialize(new { context_uri = spotifyUri });

            logger.LogInfo("Starting playback for: {SpotifyUri}", spotifyUri);
            await _apiClient.PutStringAsync("https://api.spotify.com/v1/me/player/play", jsonContent, Encoding.UTF8,
                "application/json", cancellationToken);
            logger.LogInfo("Spotify 'play' command sent successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start Spotify playback.");
        }
    }

    /// <summary>
    ///     Pauses the current playback.
    /// </summary>
    private async Task StopPlaybackAsync()
    {
        try
        {
            logger.LogInfo("Pausing Spotify playback...");
            await _apiClient.PutAsync("https://api.spotify.com/v1/me/player/pause", CancellationToken.None);
            logger.LogInfo("Spotify 'pause' command sent successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to pause Spotify playback.");
        }
    }

    /// <summary>
    ///     Sets the playback volume.
    /// </summary>
    private async Task SetVolumeAsync(int volume)
    {
        try
        {
            volume = Math.Clamp(volume, 0, 100);
            logger.LogInfo("Setting volume to {Volume}%...", volume);
            await _apiClient.PutAsync($"https://api.spotify.com/v1/me/player/volume?volume_percent={volume}",
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set volume.");
        }
    }

    /// <summary>
    ///     Sets the shuffle mode.
    /// </summary>
    private async Task SetShuffleAsync(bool shuffle)
    {
        try
        {
            logger.LogInfo("Setting shuffle to: {Shuffle}", shuffle);
            await _apiClient.PutAsync(
                $"https://api.spotify.com/v1/me/player/shuffle?state={shuffle.ToString().ToLower()}",
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set shuffle mode.");
        }
    }

    /// <summary>
    ///     Sets the repeat mode.
    /// </summary>
    private async Task SetRepeatModeAsync(string mode)
    {
        try
        {
            logger.LogInfo("Setting repeat mode to: {Mode}", mode);
            await _apiClient.PutAsync($"https://api.spotify.com/v1/me/player/repeat?state={mode}",
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set repeat mode.");
        }
    }
}