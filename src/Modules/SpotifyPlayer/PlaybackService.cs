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

    private List<KeyValuePair<string, string>> _cachedDevices = [];
    private List<KeyValuePair<string, string>> _cachedPlaylists = [];
    private List<KeyValuePair<string, string>> _cachedAlbums = [];
    private string _cachedLikedSongsUri = string.Empty;

    public PlaybackService(IModuleLogger logger, Settings settings, AuthService authService,
        SpotifyApiService apiService)
    {
        _logger = logger;
        _settings = settings;
        _authService = authService;
        _apiService = apiService;

        _settings.PlaybackContext.Value
            .Select(v => v == Settings.CustomUrlValue)
            .Subscribe(_settings.CustomUrl.SetVisibility)
            .DisposeWith(_disposables);

        _authService.AuthenticationStateChanged += OnAuthenticationStateChanged;

        _settings.UpdateAction.Invoked
            .SelectMany(_ => Observable.FromAsync(LoadDynamicChoicesAsync))
            .Subscribe(
                _ => { },
                ex => _logger.LogError(ex, "Failed to update Spotify data via Update action."))
            .DisposeWith(_disposables);

        _settings.TargetDevice.Value
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(_ => RebuildDeviceChoices())
            .DisposeWith(_disposables);

        _settings.PlaybackContext.Value
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(_ => RebuildPlaybackChoices())
            .DisposeWith(_disposables);
    }

    public async Task InitializeAsync()
    {
        _authService.RefreshUiState();

        if (!_authService.HasRefreshToken())
        {
            ClearChoices();
            return;
        }

        try
        {
            await LoadDynamicChoicesAsync();
            _logger.LogInfo("Dynamic choices loaded on initialization.");
        }
        catch (Exception)
        {
            _logger.LogWarning("Failed to load dynamic choices on initialization. Falling back to offline state.");
            RebuildDeviceChoices();
            RebuildPlaybackChoices();
        }
    }

    public Task<ValidationResult> ValidateSettingsAsync()
    {
        return _settings.ValidateAsync();
    }

    public async Task OnSessionStartAsync()
    {
        if (!_authService.HasRefreshToken())
        {
            _logger.LogError(null, "Cannot start session: Not authenticated. Please login via the module settings.");
            return;
        }

        var selectedDeviceId = _settings.TargetDevice.GetCurrentValue();

        if (string.IsNullOrWhiteSpace(selectedDeviceId))
        {
            _logger.LogWarning("No target device selected, attempting to find the first active device...");
            var devices = await _apiService.GetAvailableDevicesAsync();
            var firstDevice = devices.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstDevice.Key))
            {
                _logger.LogError(null, "Session start failed: No available devices found for Spotify playback.");
                return;
            }

            selectedDeviceId = firstDevice.Key;
            _logger.LogWarning("Defaulting to first available device: {DeviceName}", firstDevice.Value);
        }
        
        _logger.LogInfo("Configuring Spotify playback options for device {DeviceId}...", selectedDeviceId);
        
        try
        {
            var setupTasks = new List<Task>
            {
                _apiService.SetVolumeAsync(selectedDeviceId, _settings.Volume.GetCurrentValue()),
                _apiService.SetShuffleAsync(selectedDeviceId, _settings.Shuffle.GetCurrentValue() == "true"),
                _apiService.SetRepeatModeAsync(selectedDeviceId, _settings.RepeatMode.GetCurrentValue())
            };
            await Task.WhenAll(setupTasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "One or more playback options failed to apply.");
        }

        var contextSelection = _settings.PlaybackContext.GetCurrentValue();
        var urlToPlay = contextSelection == Settings.CustomUrlValue
            ? _settings.CustomUrl.GetCurrentValue()
            : contextSelection;

        if (string.IsNullOrWhiteSpace(urlToPlay))
        {
            _logger.LogWarning("No playback source selected. Nothing to play.");
            return;
        }

        await StartPlaybackAsync(urlToPlay, selectedDeviceId);
    }

    public Task OnSessionEndAsync()
    {
        if (!_authService.HasRefreshToken())
        {
            return Task.CompletedTask;
        }

        return _apiService.PauseAsync();
    }

    private async Task LoadDynamicChoicesAsync()
    {
        var devicesTask = _apiService.GetAvailableDevicesAsync();
        var playlistsTask = _apiService.GetPlaylistsAsync();
        var albumsTask = _apiService.GetSavedAlbumsAsync();
        var likedSongsTask = _apiService.GetLikedSongsAsUriListAsync();

        await Task.WhenAll(devicesTask, playlistsTask, albumsTask, likedSongsTask);

        _cachedDevices = await devicesTask;
        _cachedPlaylists = await playlistsTask;
        _cachedAlbums = await albumsTask;
        _cachedLikedSongsUri = await likedSongsTask;

        RebuildDeviceChoices();
        RebuildPlaybackChoices();
    }

    private void RebuildDeviceChoices()
    {
        var currentSelectedId = _settings.TargetDevice.GetCurrentValue();

        var finalChoices = _cachedDevices
            .Select(d => new KeyValuePair<string, string>(d.Key, d.Value))
            .ToList();

        var selectionExists = finalChoices.Any(d => d.Key == currentSelectedId);

        if (!string.IsNullOrWhiteSpace(currentSelectedId) && !selectionExists)
        {
            finalChoices.Insert(0, new KeyValuePair<string, string>(currentSelectedId, $"{currentSelectedId} (Offline)"));
        }

        if (finalChoices.Count == 0)
        {
            finalChoices.Add(new KeyValuePair<string, string>("", "No devices found."));
        }

        _settings.TargetDevice.SetChoices(finalChoices);

        if (!string.IsNullOrWhiteSpace(currentSelectedId))
        {
            _settings.TargetDevice.SetValue(currentSelectedId);
        }
        else if (_cachedDevices.Count > 0)
        {
            _settings.TargetDevice.SetValue(_cachedDevices[0].Key);
        }
    }

    private void RebuildPlaybackChoices()
    {
        var currentContext = _settings.PlaybackContext.GetCurrentValue();
        
        var finalChoicesDict = new Dictionary<string, string>
        {
            [Settings.CustomUrlValue] = "Enter a custom URL..."
        };

        if (!string.IsNullOrWhiteSpace(_cachedLikedSongsUri))
        {
            finalChoicesDict[_cachedLikedSongsUri] = "Liked Songs";
        }

        foreach (var playlist in _cachedPlaylists)
        {
            if (!string.IsNullOrWhiteSpace(playlist.Key))
                finalChoicesDict[playlist.Key] = playlist.Value;
        }

        foreach (var album in _cachedAlbums)
        {
            if (!string.IsNullOrWhiteSpace(album.Key))
                finalChoicesDict[album.Key] = album.Value;
        }

        if (!string.IsNullOrWhiteSpace(currentContext) && !finalChoicesDict.ContainsKey(currentContext))
        {
            finalChoicesDict[currentContext] = $"{currentContext} (Saved)";
        }

        var finalChoicesList = finalChoicesDict
            .Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value))
            .ToList();

        _settings.PlaybackContext.SetChoices(finalChoicesList);

        if (!string.IsNullOrWhiteSpace(currentContext))
        {
            _settings.PlaybackContext.SetValue(currentContext);
        }
    }

    private void ClearChoices()
    {
        _cachedDevices.Clear();
        _cachedPlaylists.Clear();
        _cachedAlbums.Clear();
        _cachedLikedSongsUri = string.Empty;
        
        RebuildDeviceChoices();
        RebuildPlaybackChoices();
    }

    private async Task StartPlaybackAsync(string context, string deviceId)
    {
        try
        {
            _logger.LogInfo("Starting playback for context: {Context}", context);

            if (context.StartsWith('{'))
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
        return segments.Length < 2
            ? throw new ArgumentException("URL does not contain a valid Spotify type and ID")
            : $"spotify:{segments[0]}:{segments[1]}";
    }

    private void OnAuthenticationStateChanged(bool isAuthenticated)
    {
        if (isAuthenticated)
        {
            _ = LoadDynamicChoicesAsync();
        }
        else
        {
            // Don't clear settings on logout to preserve preset data
            ClearChoices();
        }
    }

    public void Dispose()
    {
        _authService.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        _disposables.Dispose();
    }
}