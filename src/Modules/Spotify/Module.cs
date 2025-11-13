using System.Diagnostics;
using System.Net;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Http;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.Spotify;

/// <summary>
///     A module to control Spotify playback, with fully automated authentication and playback options.
/// </summary>
public class Module : IModule
{
    private readonly IModuleLogger _logger;
    private readonly IHttpClient _apiClient;
    private readonly IHttpClient _authClient;
    private readonly ISecureStorageService _secureStorage;
    private readonly CompositeDisposable _disposables = [];
    private readonly SemaphoreSlim _tokenRefreshSemaphore = new(1, 1);
    private string? _inMemoryAccessToken;

    private readonly Dictionary<string, string> _knownDevices = new(StringComparer.OrdinalIgnoreCase);

    private const string RefreshTokenKey = "SpotifyRefreshToken";
    private const string CustomUrlValue = "custom";
    private const string SpotifyClientId = "b9335aa114364ba8b957b44d33bb735d"; // Public client ID for Axorith
    private const string RedirectUri = "http://127.0.0.1:8888/callback/";

    private readonly Action _loginAction;

    private readonly Action _logoutAction;

    private readonly Setting<string> _authStatus;
    private readonly Setting<string> _targetDevice;
    private readonly Setting<string> _playbackContext;
    private readonly Setting<string> _customUrl;
    private readonly Setting<decimal> _volume;
    private readonly Setting<string> _shuffle;
    private readonly Setting<string> _repeatMode;

    public Module(IModuleLogger logger, IHttpClientFactory httpClientFactory, ISecureStorageService secureStorage,
        ModuleDefinition definition)
    {
        _logger = logger;
        _secureStorage = secureStorage;
        _apiClient = httpClientFactory.CreateClient($"{definition.Name}.Api");
        _authClient = httpClientFactory.CreateClient($"{definition.Name}.Auth");

        _loginAction = Action.Create(key: "Login", label: "Login to Spotify");
        _logoutAction = Action.Create(key: "Logout", label: "Logout", isEnabled: false);

        _authStatus = Setting.AsText(key: "AuthStatus", label: "Authentication", defaultValue: "", isReadOnly: true);

        _targetDevice = Setting.AsChoice(key: "TargetDevice", label: "Target Device", defaultValue: "",
            initialChoices: [], description: "Device to start playback on.");
        _playbackContext = Setting.AsChoice(key: "PlaybackContext", label: "Playback Source",
            defaultValue: CustomUrlValue,
            initialChoices: [new KeyValuePair<string, string>(CustomUrlValue, "Enter a custom URL...")],
            description: "Select a source or enter a custom URL.");
        _customUrl = Setting.AsText(key: "CustomUrl", label: "Custom URL", defaultValue: "",
            description: "URL of the track, playlist, or album to play.", isVisible: false);

        _volume = Setting.AsNumber(key: "Volume", label: "Volume", defaultValue: 80,
            description: "Playback volume (0-100).");
        _shuffle = Setting.AsChoice(key: "Shuffle", label: "Shuffle Mode", defaultValue: "false",
            initialChoices:
            [new KeyValuePair<string, string>("true", "On"), new KeyValuePair<string, string>("false", "Off")]);
        _repeatMode = Setting.AsChoice(key: "RepeatMode", label: "Repeat Mode", defaultValue: "off",
            initialChoices:
            [
                new KeyValuePair<string, string>("off", "Off"),
                new KeyValuePair<string, string>("context", "Repeat Playlist/Album"),
                new KeyValuePair<string, string>("track", "Repeat Track")
            ]);

        var hasRefreshToken = !string.IsNullOrWhiteSpace(_secureStorage.RetrieveSecret(RefreshTokenKey));
        UpdateUiForAuthenticationState(hasRefreshToken);

        _playbackContext.Value.Select(v => v == CustomUrlValue).Subscribe(_customUrl.SetVisibility)
            .DisposeWith(_disposables);

        _loginAction.Invoked.SelectMany(_ => PerformPkceLoginAsync()).Subscribe(success =>
        {
            UpdateUiForAuthenticationState(success);
            if (success) _ = LoadDynamicChoicesAsync();
        }).DisposeWith(_disposables);

        _logoutAction.Invoked.Subscribe(_ =>
        {
            Logout();
            UpdateUiForAuthenticationState(false);
        }).DisposeWith(_disposables);
    }

    private void UpdateUiForAuthenticationState(bool isAuthenticated)
    {
        if (isAuthenticated)
        {
            _authStatus.SetValue("Authenticated ✓");
            _loginAction.SetLabel("Re-Login with Spotify");
        }
        else
        {
            _authStatus.SetValue("⚠ Login required");
            _loginAction.SetLabel("Login to Spotify");
        }

        _logoutAction.SetEnabled(isAuthenticated);
        _customUrl.SetVisibility(_playbackContext.GetCurrentValue() == CustomUrlValue);
    }

    /// <inheritdoc />
    public Type? CustomSettingsViewType => null;

    /// <inheritdoc />
    public object? GetSettingsViewModel()
    {
        return null;
    }

    public IReadOnlyList<ISetting> GetSettings()
    {
        return
        [
            _authStatus, _targetDevice, _playbackContext,
            _customUrl, _volume, _shuffle, _repeatMode
        ];
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return [_loginAction, _logoutAction];
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var hasRefreshToken = !string.IsNullOrWhiteSpace(_secureStorage.RetrieveSecret(RefreshTokenKey));
        if (hasRefreshToken)
            try
            {
                await LoadDynamicChoicesAsync();
                _logger.LogInfo("Dynamic choices loaded successfully in InitializeAsync");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to load dynamic choices: {Message}", ex.Message);
                // Non-fatal error - module can still function without dynamic data
            }
    }

    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        var deviceId = _targetDevice.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(deviceId))
            return Task.FromResult(ValidationResult.Fail("Target device must be selected"));

        return Task.FromResult(ValidationResult.Success);
    }

    public async Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        var accessToken = await GetValidAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogError(null,
                "Could not obtain a valid Spotify Access Token. Please login via the module settings.");
            return;
        }

        _apiClient.AddDefaultHeader("Authorization", $"Bearer {accessToken}");

        var deviceId = _targetDevice.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            var devices = await GetAvailableDevicesAsync();
            deviceId = devices.FirstOrDefault().Key;
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                _logger.LogError(null, "No available devices found for Spotify playback.");
                return;
            }

            _logger.LogWarning("No target device selected, defaulting to first available device: {DeviceName}",
                devices.FirstOrDefault().Value);
        }

        _logger.LogInfo("Configuring playback options...");
        try
        {
            var setupTasks = new List<Task>
            {
                SetVolumeAsync((int)_volume.GetCurrentValue(), deviceId),
                SetShuffleAsync(_shuffle.GetCurrentValue() == "true", deviceId),
                SetRepeatModeAsync(_repeatMode.GetCurrentValue(), deviceId)
            };

            await Task.WhenAll(setupTasks);
            _logger.LogInfo("Playback options configured successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "One or more playback options failed to apply.");
        }

        var contextSelection = _playbackContext.GetCurrentValue();
        var urlToPlay = contextSelection == CustomUrlValue ? _customUrl.GetCurrentValue() : contextSelection;

        if (!string.IsNullOrWhiteSpace(urlToPlay)) await StartPlaybackAsync(urlToPlay, deviceId, cancellationToken);
    }

    public async Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var accessToken = await GetValidAccessTokenAsync();
            if (string.IsNullOrWhiteSpace(accessToken)) return;
            _apiClient.AddDefaultHeader("Authorization", $"Bearer {accessToken}");

            var deviceId = _targetDevice.GetCurrentValue();
            if (string.IsNullOrWhiteSpace(deviceId)) return;

            await StopPlaybackAsync(deviceId);
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to stop Spotify playback during session end");
            // Suppress exception to avoid breaking ReactiveUI pipeline
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        _tokenRefreshSemaphore.Dispose();
    }

    /// <summary>
    ///     Logout and clear all stored tokens.
    /// </summary>
    private void Logout()
    {
        _secureStorage.DeleteSecret(RefreshTokenKey);
        _inMemoryAccessToken = null;
        UpdateUiForAuthenticationState(false);
        _logger.LogInfo("Logged out from Spotify");
    }

    private async Task LoadDynamicChoicesAsync()
    {
        var accessToken = await GetValidAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(accessToken)) return;
        _apiClient.AddDefaultHeader("Authorization", $"Bearer {accessToken}");

        var devicesTask = LoadDevicesAsync();
        var itemsTask = LoadPlayableItemsAsync();
        await Task.WhenAll(devicesTask, itemsTask);
    }

    private async Task LoadDevicesAsync()
    {
        try
        {
            var devices = await GetAvailableDevicesAsync();
            UpdateDeviceChoices(devices);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Spotify devices.");
            UpdateDeviceChoices([]);
        }
    }

    private async Task<List<KeyValuePair<string, string>>> GetAvailableDevicesAsync()
    {
        var responseJson = await _apiClient.GetStringAsync("https://api.spotify.com/v1/me/player/devices");
        using var jsonDoc = JsonDocument.Parse(responseJson);
        return jsonDoc.RootElement.GetProperty("devices").EnumerateArray()
            .Select(d => new KeyValuePair<string, string>(
                d.GetProperty("id").GetString() ?? "",
                $"{d.GetProperty("name").GetString()} ({d.GetProperty("type").GetString()})"))
            .ToList();
    }

    private void UpdateDeviceChoices(IReadOnlyList<KeyValuePair<string, string>> freshDevices)
    {
        var merged = new List<KeyValuePair<string, string>>();

        foreach (var (id, label) in freshDevices)
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;

            _knownDevices[id] = label;
            merged.Add(new KeyValuePair<string, string>(id, label));
        }

        var selectedDeviceId = _targetDevice.GetCurrentValue();
        if (!string.IsNullOrWhiteSpace(selectedDeviceId) && merged.All(d => d.Key != selectedDeviceId))
        {
            var offlineLabel = _knownDevices.TryGetValue(selectedDeviceId, out var knownName)
                ? $"{knownName} (offline)"
                : "Previously selected device (offline)";
            merged.Insert(0, new KeyValuePair<string, string>(selectedDeviceId, offlineLabel));
        }

        if (merged.Count == 0)
            merged.Add(new KeyValuePair<string, string>(string.Empty,
                "No active devices found. Start Spotify on a device to enable playback."));

        _targetDevice.SetChoices(merged);
        _logger.LogInfo("Device choices updated. Active: {Active}, Selected: {Selected}",
            merged.Count, selectedDeviceId);
    }

    private async Task LoadPlayableItemsAsync()
    {
        try
        {
            var playlistsTask = _apiClient.GetStringAsync("https://api.spotify.com/v1/me/playlists");
            var albumsTask = _apiClient.GetStringAsync("https://api.spotify.com/v1/me/albums");
            var tracksTask = _apiClient.GetStringAsync("https://api.spotify.com/v1/me/tracks?limit=50");
            await Task.WhenAll(playlistsTask, albumsTask, tracksTask);

            var choices = new List<KeyValuePair<string, string>> { new(CustomUrlValue, "Enter a custom URL...") };

            using (var doc = JsonDocument.Parse(await tracksTask))
            {
                var tracks = doc.RootElement.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("track"));
                choices.Add(new KeyValuePair<string, string>(
                    JsonSerializer.Serialize(new { uris = tracks.Select(t => t.GetProperty("uri").GetString()) }),
                    "Liked Songs"));
            }

            using (var doc = JsonDocument.Parse(await playlistsTask))
            {
                choices.AddRange(doc.RootElement.GetProperty("items").EnumerateArray().Select(p =>
                    new KeyValuePair<string, string>(p.GetProperty("uri").GetString() ?? "",
                        $"{p.GetProperty("name").GetString()} (Playlist)")));
            }

            using (var doc = JsonDocument.Parse(await albumsTask))
            {
                choices.AddRange(doc.RootElement.GetProperty("items").EnumerateArray().Select(a =>
                    new KeyValuePair<string, string>(a.GetProperty("album").GetProperty("uri").GetString() ?? "",
                        $"{a.GetProperty("album").GetProperty("name").GetString()} (Album)")));
            }

            _playbackContext.SetChoices(choices);
            _logger.LogInfo("Successfully loaded playable items from Spotify.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load playable items from Spotify.");
        }
    }

    private async Task<string?> GetValidAccessTokenAsync()
    {
        if (!string.IsNullOrWhiteSpace(_inMemoryAccessToken)) return _inMemoryAccessToken;

        await _tokenRefreshSemaphore.WaitAsync();
        try
        {
            if (!string.IsNullOrWhiteSpace(_inMemoryAccessToken)) return _inMemoryAccessToken;

            var refreshToken = _secureStorage.RetrieveSecret(RefreshTokenKey);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                _logger.LogWarning("Refresh Token not found. A login is required via the module settings.");
                return null;
            }

            _logger.LogInfo("Access token expired or not found. Refreshing...");

            try
            {
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "grant_type", "refresh_token" },
                    { "refresh_token", refreshToken },
                    { "client_id", SpotifyClientId }
                });

                var responseJson = await _authClient.PostStringAsync("https://accounts.spotify.com/api/token",
                    await content.ReadAsStringAsync(),
                    Encoding.UTF8, "application/x-www-form-urlencoded");
                using var jsonDoc = JsonDocument.Parse(responseJson);
                var newAccessToken = jsonDoc.RootElement.GetProperty("access_token").GetString();

                if (jsonDoc.RootElement.TryGetProperty("refresh_token", out var newRefreshTokenElement))
                {
                    var newRefreshToken = newRefreshTokenElement.GetString();
                    if (!string.IsNullOrWhiteSpace(newRefreshToken))
                    {
                        _logger.LogInfo("Spotify provided a new rotated refresh token. Updating secure storage.");
                        _secureStorage.StoreSecret(RefreshTokenKey, newRefreshToken);
                    }
                }

                if (string.IsNullOrWhiteSpace(newAccessToken))
                {
                    _logger.LogError(null, "Failed to refresh token: API response did not contain an access_token.");
                    return null;
                }

                _logger.LogInfo("Successfully refreshed access token.");
                _inMemoryAccessToken = newAccessToken;
                return _inMemoryAccessToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to refresh token. The refresh token might be invalid. Please login again.");
                UpdateUiForAuthenticationState(false);
                return null;
            }
        }
        finally
        {
            _tokenRefreshSemaphore.Release();
        }
    }

    private async Task<bool> PerformPkceLoginAsync()
    {
        var (codeVerifier, codeChallenge) = GeneratePkcePair();

        using var listener = new HttpListener();
        listener.Prefixes.Add(RedirectUri);

        try
        {
            listener.Start();
            var scopes =
                "user-modify-playback-state user-read-playback-state user-read-private playlist-read-private user-library-read";
            var authUrl = $"https://accounts.spotify.com/authorize?client_id={SpotifyClientId}" +
                          "&response_type=code" +
                          $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                          $"&scope={Uri.EscapeDataString(scopes)}" +
                          "&code_challenge_method=S256" +
                          $"&code_challenge={codeChallenge}";

            try
            {
                _logger.LogInfo("Attempting to open browser for Spotify login.");
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
                _logger.LogWarning("Your browser has been opened to log in to Spotify. Please grant access.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to open browser automatically. Please copy and paste this URL into your browser: {Url}",
                    authUrl);
                _authStatus.SetValue("Error: Could not open browser. See logs.");
                return false;
            }

            var contextTask = listener.GetContextAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
            var completedTask = await Task.WhenAny(contextTask, timeoutTask).ConfigureAwait(false);

            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("Spotify authentication timed out after 5 minutes");
                _authStatus.SetValue("Error: Authentication timed out");
                listener.Stop();
                return false;
            }

            var context = await contextTask.ConfigureAwait(false);
            var code = context.Request.QueryString.Get("code");

            var response = context.Response;
            var buffer = "<html><body><h1>Success!</h1><p>You can now close this browser tab.</p></body></html>"u8
                .ToArray();
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer);
            response.OutputStream.Close();
            listener.Stop();

            if (string.IsNullOrWhiteSpace(code)) return false;

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code },
                { "redirect_uri", RedirectUri },
                { "client_id", SpotifyClientId },
                { "code_verifier", codeVerifier }
            });

            var responseJson = await _authClient.PostStringAsync("https://accounts.spotify.com/api/token",
                await content.ReadAsStringAsync(),
                Encoding.UTF8, "application/x-www-form-urlencoded");

            using var jsonDoc = JsonDocument.Parse(responseJson);
            var refreshToken = jsonDoc.RootElement.GetProperty("refresh_token").GetString();

            if (string.IsNullOrWhiteSpace(refreshToken)) return false;

            _secureStorage.StoreSecret(RefreshTokenKey, refreshToken);
            _logger.LogInfo("SUCCESS: New Refresh Token has been obtained and saved securely.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed during PKCE login process.");
            return false;
        }
        finally
        {
            if (listener.IsListening) listener.Stop();
        }
    }

    private static (string, string) GeneratePkcePair()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var codeVerifier = Convert.ToBase64String(randomBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        var codeChallenge = Convert.ToBase64String(challengeBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return (codeVerifier, codeChallenge);
    }

    private async Task StartPlaybackAsync(string context, string deviceId, CancellationToken cancellationToken)
    {
        try
        {
            string jsonContent;
            if (context.StartsWith("{"))
            {
                jsonContent = context;
            }
            else
            {
                var spotifyUri = ConvertUrlToUri(context);
                jsonContent = spotifyUri.Contains(":track:")
                    ? JsonSerializer.Serialize(new { uris = new[] { spotifyUri } })
                    : JsonSerializer.Serialize(new { context_uri = spotifyUri });
            }

            _logger.LogInfo("Starting playback for context: {Context}", context);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            await _apiClient.PutStringAsync($"https://api.spotify.com/v1/me/player/play?device_id={deviceId}",
                jsonContent, Encoding.UTF8, "application/json", cts.Token);
            _logger.LogInfo("Spotify 'play' command sent successfully.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(null, "Failed to start Spotify playback: request timed out after 10 seconds.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Spotify playback.");
        }
    }

    private static string ConvertUrlToUri(string url)
    {
        if (url.Contains("spotify:")) return url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) throw new ArgumentException("Invalid URL format");
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2) throw new ArgumentException("URL does not contain a valid Spotify type and ID");
        return $"spotify:{segments[0]}:{segments[1]}";
    }

    private async Task StopPlaybackAsync(string deviceId)
    {
        try
        {
            _logger.LogInfo("Pausing Spotify playback...");
            await _apiClient.PutAsync($"https://api.spotify.com/v1/me/player/pause?device_id={deviceId}",
                CancellationToken.None);
            _logger.LogInfo("Spotify 'pause' command sent successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause Spotify playback.");
        }
    }

    private async Task SetVolumeAsync(int volume, string deviceId)
    {
        try
        {
            volume = Math.Clamp(volume, 0, 100);
            _logger.LogInfo("Setting volume to {Volume}%...", volume);
            await _apiClient.PutAsync(
                $"https://api.spotify.com/v1/me/player/volume?volume_percent={volume}&device_id={deviceId}",
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set volume.");
        }
    }

    private async Task SetShuffleAsync(bool shuffle, string deviceId)
    {
        try
        {
            _logger.LogInfo("Setting shuffle to: {Shuffle}", shuffle);
            await _apiClient.PutAsync(
                $"https://api.spotify.com/v1/me/player/shuffle?state={shuffle.ToString().ToLower()}&device_id={deviceId}",
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set shuffle mode.");
        }
    }

    private async Task SetRepeatModeAsync(string mode, string deviceId)
    {
        try
        {
            _logger.LogInfo("Setting repeat mode to: {Mode}", mode);
            await _apiClient.PutAsync($"https://api.spotify.com/v1/me/player/repeat?state={mode}&device_id={deviceId}",
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set repeat mode.");
        }
    }
}