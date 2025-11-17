using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Text.Json;
using Axorith.Sdk;
using Axorith.Sdk.Logging;

namespace Axorith.Module.SpotifyPlayer;

internal sealed class PlaybackService : IDisposable
{
    private readonly IModuleLogger _logger;
    private readonly Settings _settings;
    private readonly AuthService _authService;
    private readonly SpotifyApiService _apiService;
    private readonly CompositeDisposable _disposables = [];

    public PlaybackService(IModuleLogger logger, Settings settings, AuthService authService,
        SpotifyApiService apiService)
    {
        _logger = logger;
        _settings = settings;
        _authService = authService;
        _apiService = apiService;

        _settings.PlaybackContext.Value
            .Select(v => v == Settings.CUSTOM_URL_VALUE)
            .Subscribe(_settings.CustomUrl.SetVisibility)
            .DisposeWith(_disposables);

        _authService.AuthenticationStateChanged += OnAuthenticationStateChanged;

        _settings.UpdateAction.Invoked
            .SelectMany(_ => Observable.FromAsync(LoadDynamicChoicesAsync))
            .Subscribe(
                _ => { },
                ex => _logger.LogError(ex, "Failed to update Spotify data via Update action."))
            .DisposeWith(_disposables);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!_authService.HasRefreshToken()) return;

        try
        {
            await LoadDynamicChoicesAsync();
            _logger.LogInfo("Dynamic choices loaded on initialization.");
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to load dynamic choices on initialization.");
        }
    }

    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        return _settings.ValidateAsync(cancellationToken);
    }

    public async Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        var waitMs = _settings.WaitTime.GetCurrentValue();
        if (waitMs > 0) await Task.Delay(TimeSpan.FromMilliseconds(waitMs), cancellationToken);

        if (!_authService.HasRefreshToken())
        {
            _logger.LogError(null, "Cannot start session: Not authenticated. Please login via the module settings.");
            return;
        }

        var selectedDeviceName = _settings.TargetDevice.GetCurrentValue();
        string? deviceId;

        if (string.IsNullOrWhiteSpace(selectedDeviceName))
        {
            _logger.LogWarning("No target device selected, attempting to find the first active device...");
            var devices = await _apiService.GetAvailableDevicesAsync();
            var firstDevice = devices.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstDevice.Key))
            {
                _logger.LogError(null, "Session start failed: No available devices found for Spotify playback.");
                return;
            }

            deviceId = firstDevice.Key;
            _logger.LogWarning("Defaulting to first available device: {DeviceName}", firstDevice.Value);
        }
        else
        {
            _logger.LogInfo("Looking for selected device: '{DeviceName}'...", selectedDeviceName);
            var devices = await _apiService.GetAvailableDevicesAsync();
            var targetDevice = devices.FirstOrDefault(d => d.Value.StartsWith(selectedDeviceName));

            if (string.IsNullOrWhiteSpace(targetDevice.Key))
            {
                _logger.LogError(null,
                    "Session start failed: Selected device '{DeviceName}' is offline or could not be found.",
                    selectedDeviceName);
                return;
            }

            deviceId = targetDevice.Key;
            _logger.LogInfo("Found active device with ID: {DeviceId}", deviceId);
        }

        _logger.LogInfo("Configuring Spotify playback options for device {DeviceId}...", deviceId);
        try
        {
            var setupTasks = new List<Task>
            {
                _apiService.SetVolumeAsync(deviceId, _settings.Volume.GetCurrentValue()),
                _apiService.SetShuffleAsync(deviceId, _settings.Shuffle.GetCurrentValue() == "true"),
                _apiService.SetRepeatModeAsync(deviceId, _settings.RepeatMode.GetCurrentValue())
            };
            await Task.WhenAll(setupTasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "One or more playback options failed to apply.");
        }

        var contextSelection = _settings.PlaybackContext.GetCurrentValue();
        var urlToPlay = contextSelection == Settings.CUSTOM_URL_VALUE
            ? _settings.CustomUrl.GetCurrentValue()
            : contextSelection;

        if (string.IsNullOrWhiteSpace(urlToPlay))
        {
            _logger.LogWarning("No playback source selected. Nothing to play.");
            return;
        }

        await StartPlaybackAsync(urlToPlay, deviceId);
    }

    public Task OnSessionEndAsync(CancellationToken cancellationToken)
    {
        if (!_authService.HasRefreshToken()) return Task.CompletedTask;

        var deviceId = _settings.TargetDevice.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(deviceId)) return Task.CompletedTask;

        return _apiService.PauseAsync(deviceId);
    }

    private async Task LoadDynamicChoicesAsync()
    {
        var devicesTask = _apiService.GetAvailableDevicesAsync();
        var playlistsTask = _apiService.GetPlaylistsAsync();
        var albumsTask = _apiService.GetSavedAlbumsAsync();
        var likedSongsTask = _apiService.GetLikedSongsAsUriListAsync();

        await Task.WhenAll(devicesTask, playlistsTask, albumsTask, likedSongsTask);

        UpdateDeviceChoices(await devicesTask);
        UpdatePlayableItemChoices(await playlistsTask, await albumsTask, await likedSongsTask);
    }

    private void UpdateDeviceChoices(IReadOnlyList<KeyValuePair<string, string>> freshDevices)
    {
        var selectedDeviceName = _settings.TargetDevice.GetCurrentValue();

        var finalChoices = freshDevices
            .Select(d => new KeyValuePair<string, string>(d.Value, d.Value))
            .ToList();

        var selectionExists = finalChoices.Any(d => d.Key == selectedDeviceName);

        if (!string.IsNullOrWhiteSpace(selectedDeviceName) && !selectionExists)
            finalChoices.Insert(0,
                new KeyValuePair<string, string>(selectedDeviceName, $"{selectedDeviceName} (Offline)"));

        if (finalChoices.Count == 0) finalChoices.Add(new KeyValuePair<string, string>("", "No devices found."));

        _settings.TargetDevice.SetChoices(finalChoices);

        if (!string.IsNullOrWhiteSpace(selectedDeviceName)) _settings.TargetDevice.SetValue(selectedDeviceName);
    }

    private void UpdatePlayableItemChoices(
        IReadOnlyList<KeyValuePair<string, string>> playlists,
        IReadOnlyList<KeyValuePair<string, string>> albums,
        string likedSongsUri)
    {
        var selectedContext = _settings.PlaybackContext.GetCurrentValue();
        var finalChoicesDict = new Dictionary<string, string>
        {
            [Settings.CUSTOM_URL_VALUE] = "Enter a custom URL..."
        };

        if (!string.IsNullOrWhiteSpace(likedSongsUri)) finalChoicesDict[likedSongsUri] = "Liked Songs";

        foreach (var playlist in playlists)
            if (!string.IsNullOrWhiteSpace(playlist.Key))
                finalChoicesDict[playlist.Key] = playlist.Value;

        foreach (var album in albums)
            if (!string.IsNullOrWhiteSpace(album.Key))
                finalChoicesDict[album.Key] = album.Value;

        if (!string.IsNullOrWhiteSpace(selectedContext) && !finalChoicesDict.ContainsKey(selectedContext))
            finalChoicesDict[selectedContext] = $"{selectedContext} (Saved)";

        var finalChoicesList = finalChoicesDict.Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value))
            .ToList();

        _settings.PlaybackContext.SetChoices(finalChoicesList);

        if (!string.IsNullOrWhiteSpace(selectedContext)) _settings.PlaybackContext.SetValue(selectedContext);
    }

    private async Task StartPlaybackAsync(string context, string deviceId)
    {
        try
        {
            _logger.LogInfo("Starting playback for context: {Context}", context);

            if (context.StartsWith("{", StringComparison.Ordinal))
            {
                var uris = JsonSerializer.Deserialize<JsonElement>(context).GetProperty("uris").EnumerateArray()
                    .Select(e => e.GetString()).ToList();
                await _apiService.PlayAsync(deviceId, string.Empty, uris!);
            }
            else
            {
                var spotifyUri = ConvertUrlToUri(context);
                await _apiService.PlayAsync(deviceId, spotifyUri);
            }

            _logger.LogInfo("Spotify 'play' command sent successfully.");
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

    private void OnAuthenticationStateChanged(bool isAuthenticated)
    {
        if (isAuthenticated)
        {
            _ = LoadDynamicChoicesAsync();
        }
        else
        {
            UpdateDeviceChoices(Array.Empty<KeyValuePair<string, string>>());
            UpdatePlayableItemChoices(Array.Empty<KeyValuePair<string, string>>(),
                Array.Empty<KeyValuePair<string, string>>(), string.Empty);
        }
    }

    public void Dispose()
    {
        _authService.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        _disposables.Dispose();
    }
}