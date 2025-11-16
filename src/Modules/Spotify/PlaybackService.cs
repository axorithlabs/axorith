using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using Axorith.Sdk;
using Axorith.Sdk.Http;
using Axorith.Sdk.Logging;

namespace Axorith.Module.Spotify;

internal sealed class PlaybackService : IDisposable
{
    private readonly IModuleLogger _logger;
    private readonly IHttpClient _apiClient;
    private readonly Settings _settings;
    private readonly AuthService _authService;
    private readonly CompositeDisposable _disposables = [];
    private readonly Dictionary<string, string> _knownDevices = new(StringComparer.OrdinalIgnoreCase);

    public PlaybackService(IModuleLogger logger, IHttpClientFactory httpClientFactory,
        ModuleDefinition definition, Settings settings, AuthService authService)
    {
        _logger = logger;
        _settings = settings;
        _authService = authService;
        _apiClient = httpClientFactory.CreateClient($"{definition.Name}.Api");

        _settings.PlaybackContext.Value
            .Select(v => v == Settings.CustomUrlValue)
            .Subscribe(visible => _settings.CustomUrl.SetVisibility(visible))
            .DisposeWith(_disposables);

        _authService.AuthenticationStateChanged += OnAuthenticationStateChanged;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!_authService.HasRefreshToken()) return;

        try
        {
            await LoadDynamicChoicesAsync();
            _logger.LogInfo("Dynamic choices loaded successfully in InitializeAsync");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to load dynamic choices: {Message}", ex.Message);
        }
    }

    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        return _settings.ValidateAsync(cancellationToken);
    }

    public async Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        var waitMs = _settings.WaitTime.GetCurrentValue();
        if (waitMs > 0)
        {
            var delay = TimeSpan.FromMilliseconds(waitMs);
            _logger.LogInfo("Delaying Spotify startup for {DelayMs} ms to allow client to launch...", waitMs);
            await Task.Delay(delay, cancellationToken);
        }

        var accessToken = await _authService.GetValidAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogError(null,
                "Could not obtain a valid Spotify Access Token. Please login via the module settings.");
            return;
        }

        _apiClient.AddDefaultHeader("Authorization", $"Bearer {accessToken}");

        var deviceId = _settings.TargetDevice.GetCurrentValue();
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
                SetVolumeAsync(_settings.Volume.GetCurrentValue(), deviceId),
                SetShuffleAsync(_settings.Shuffle.GetCurrentValue() == "true", deviceId),
                SetRepeatModeAsync(_settings.RepeatMode.GetCurrentValue(), deviceId)
            };

            await Task.WhenAll(setupTasks);
            _logger.LogInfo("Playback options configured successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "One or more playback options failed to apply.");
        }

        var contextSelection = _settings.PlaybackContext.GetCurrentValue();
        var urlToPlay = contextSelection == Settings.CustomUrlValue
            ? _settings.CustomUrl.GetCurrentValue()
            : contextSelection;

        if (!string.IsNullOrWhiteSpace(urlToPlay))
            await StartPlaybackAsync(urlToPlay, deviceId, cancellationToken);
    }

    public async Task OnSessionEndAsync(CancellationToken cancellationToken)
    {
        try
        {
            var accessToken = await _authService.GetValidAccessTokenAsync();
            if (string.IsNullOrWhiteSpace(accessToken)) return;
            _apiClient.AddDefaultHeader("Authorization", $"Bearer {accessToken}");

            var deviceId = _settings.TargetDevice.GetCurrentValue();
            if (string.IsNullOrWhiteSpace(deviceId)) return;

            await StopPlaybackAsync(deviceId);
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to stop Spotify playback during session end");
        }
    }

    private async Task LoadDynamicChoicesAsync()
    {
        var accessToken = await _authService.GetValidAccessTokenAsync();
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
            UpdateDeviceChoices(Array.Empty<KeyValuePair<string, string>>());
        }
    }

    private async Task<List<KeyValuePair<string, string>>> GetAvailableDevicesAsync()
    {
        var responseJson = await _apiClient.GetStringAsync("https://api.spotify.com/v1/me/player/devices");
        using var jsonDoc = JsonDocument.Parse(responseJson);
        return jsonDoc.RootElement.GetProperty("devices").EnumerateArray()
            .Select(d => new KeyValuePair<string, string>(
                d.GetProperty("id").GetString() ?? string.Empty,
                $"{d.GetProperty("name").GetString()} ({d.GetProperty("type").GetString()})"))
            .ToList();
    }

    private void UpdateDeviceChoices(IReadOnlyList<KeyValuePair<string, string>> freshDevices)
    {
        var merged = new List<KeyValuePair<string, string>>();

        foreach (var pair in freshDevices)
        {
            var id = pair.Key;
            var label = pair.Value;

            if (string.IsNullOrWhiteSpace(id))
                continue;

            _knownDevices[id] = label;
            merged.Add(new KeyValuePair<string, string>(id, label));
        }

        var selectedDeviceId = _settings.TargetDevice.GetCurrentValue();
        if (!string.IsNullOrWhiteSpace(selectedDeviceId))
        {
            var selectedFromFresh = merged.FirstOrDefault(d => d.Key == selectedDeviceId);
            if (selectedFromFresh.Key == selectedDeviceId)
                _settings.TargetDeviceLabel.SetValue(selectedFromFresh.Value);

            if (merged.All(d => d.Key != selectedDeviceId))
            {
                var baseLabel = _knownDevices.TryGetValue(selectedDeviceId, out var knownName)
                    ? knownName
                    : _settings.TargetDeviceLabel.GetCurrentValue();

                var label = !string.IsNullOrWhiteSpace(baseLabel)
                    ? baseLabel
                    : selectedDeviceId;

                merged.Insert(0, new KeyValuePair<string, string>(selectedDeviceId, label));
            }
        }

        if (merged.Count == 0)
            merged.Add(new KeyValuePair<string, string>(string.Empty,
                "No active devices found. Start Spotify on a device to enable playback."));

        _settings.TargetDevice.SetChoices(merged);
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

            var choices = new List<KeyValuePair<string, string>>
            {
                new(Settings.CustomUrlValue, "Enter a custom URL...")
            };

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
                    new KeyValuePair<string, string>(p.GetProperty("uri").GetString() ?? string.Empty,
                        $"{p.GetProperty("name").GetString()} (Playlist)")));
            }

            using (var doc = JsonDocument.Parse(await albumsTask))
            {
                choices.AddRange(doc.RootElement.GetProperty("items").EnumerateArray().Select(a =>
                    new KeyValuePair<string, string>(
                        a.GetProperty("album").GetProperty("uri").GetString() ?? string.Empty,
                        $"{a.GetProperty("album").GetProperty("name").GetString()} (Album)")));
            }

            _settings.PlaybackContext.SetChoices(choices);
            _logger.LogInfo("Successfully loaded playable items from Spotify.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load playable items from Spotify.");
        }
    }

    private async Task StartPlaybackAsync(string context, string deviceId, CancellationToken cancellationToken)
    {
        try
        {
            string jsonContent;
            if (context.StartsWith("{", StringComparison.Ordinal))
            {
                jsonContent = context;
            }
            else
            {
                var spotifyUri = ConvertUrlToUri(context);
                jsonContent = spotifyUri.Contains(":track:", StringComparison.Ordinal)
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
        if (url.Contains("spotify:", StringComparison.Ordinal)) return url;
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

    private void OnAuthenticationStateChanged(bool isAuthenticated)
    {
        if (!isAuthenticated) return;
        _ = LoadDynamicChoicesAsync();
    }

    public void Dispose()
    {
        _authService.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        _disposables.Dispose();
    }
}