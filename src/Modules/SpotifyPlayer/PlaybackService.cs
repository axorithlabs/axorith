using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Text.Json;
using Axorith.Sdk;
using Axorith.Sdk.Logging;
using Axorith.Shared.Platform;

namespace Axorith.Module.SpotifyPlayer;

internal sealed class PlaybackService : IDisposable
{
    private readonly IModuleLogger _logger;
    private readonly Settings _settings;
    private readonly AuthService _authService;
    private readonly SpotifyApiService _apiService;
    private readonly CompositeDisposable _disposables = [];
    private readonly SemaphoreSlim _choicesRefreshLock = new(1, 1);
    private readonly TimeSpan _choicesTtl = TimeSpan.FromMinutes(15);

    private List<KeyValuePair<string, string>> _cachedPlaylists = [];
    private List<KeyValuePair<string, string>> _cachedAlbums = [];
    private string _cachedLikedSongsUri = string.Empty;
    private DateTime _choicesLastUpdatedUtc = DateTime.MinValue;

    public PlaybackService(
        IModuleLogger logger,
        Settings settings,
        AuthService authService,
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

        if (TryServeCachedChoices())
        {
            _ = RefreshPlaylistsLoopAsync();
            return;
        }

        _ = RefreshChoicesAsync(force: true);

        _ = RefreshPlaylistsLoopAsync();
    }

    private async Task RefreshPlaylistsLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync())
        {
            if (_authService.HasRefreshToken())
            {
                _ = RefreshChoicesAsync();
            }
        }
    }

    public Task<ValidationResult> ValidateSettingsAsync()
    {
        return _settings.ValidateAsync();
    }

    public async Task OnSessionStartAsync(CancellationToken cancellationToken = default)
    {
        if (!_authService.HasRefreshToken())
        {
            _logger.LogError(null, "Cannot start session: Not authenticated. Please login via the module settings.");
            return;
        }

        if (_settings.EnsureSpotifyRunning.GetCurrentValue())
        {
            const string processName = "Spotify";

            var isRunning = await WaitForProcessAsync(processName, TimeSpan.FromSeconds(30), cancellationToken);

            if (!isRunning)
            {
                _logger.LogError(null, "Spotify process '{ProcessName}' not found after 30 seconds. Aborting playback.",
                    processName);
                return;
            }
        }

        string? targetDeviceId = null;

        for (var i = 0; i < 5; i++)
        {
            targetDeviceId = await ResolveTargetDeviceIdAsync();
            if (!string.IsNullOrEmpty(targetDeviceId))
            {
                break;
            }

            _logger.LogInfo("Target device not found yet. Retrying in 2s... (Attempt {Attempt}/5)", i + 1);
            await Task.Delay(2000, cancellationToken);
        }

        if (string.IsNullOrEmpty(targetDeviceId))
        {
            _logger.LogError(null, "Session start failed: Could not find a suitable Spotify device to play on.");
            return;
        }

        _logger.LogInfo("Target device resolved: {DeviceId}", targetDeviceId);

        try
        {
            var setupTasks = new List<Task>
            {
                _apiService.SetVolumeAsync(targetDeviceId, _settings.Volume.GetCurrentValue()),
                _apiService.SetShuffleAsync(targetDeviceId, _settings.Shuffle.GetCurrentValue() == "true"),
                _apiService.SetRepeatModeAsync(targetDeviceId, _settings.RepeatMode.GetCurrentValue())
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

        await StartPlaybackAsync(urlToPlay, targetDeviceId);
    }

    private async Task<string?> ResolveTargetDeviceIdAsync()
    {
        var mode = _settings.DeviceSelectionMode.GetCurrentValue();
        var devices = await _apiService.GetDevicesAsync();

        if (devices.Count == 0)
        {
            return null;
        }

        switch (mode)
        {
            case Settings.ModeLocalComputer:
                var computer =
                    devices.FirstOrDefault(d => d.Type.Equals("Computer", StringComparison.OrdinalIgnoreCase));
                if (computer != null)
                {
                    _logger.LogInfo("Found local computer: {Name} ({Id})", computer.Name, computer.Id);
                    return computer.Id;
                }

                break;

            case Settings.ModeLastActive:
                var active = devices.FirstOrDefault(d => d.IsActive);
                if (active != null)
                {
                    _logger.LogInfo("Found active device: {Name} ({Id})", active.Name, active.Id);
                    return active.Id;
                }

                var first = devices.First();
                _logger.LogInfo("No active device found, using first available: {Name}", first.Name);
                return first.Id;

            case Settings.ModeSpecificName:
                var targetName = _settings.SpecificDeviceName.GetCurrentValue();
                if (string.IsNullOrWhiteSpace(targetName))
                {
                    return null;
                }

                var specific =
                    devices.FirstOrDefault(d => d.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                if (specific != null)
                {
                    _logger.LogInfo("Found specific device: {Name} ({Id})", specific.Name, specific.Id);
                    return specific.Id;
                }

                break;
        }

        if (mode != Settings.ModeLocalComputer)
        {
            return null;
        }

        {
            var machineName = Environment.MachineName;
            var match = devices.FirstOrDefault(d => d.Name.Contains(machineName, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                return null;
            }

            _logger.LogInfo("Found device matching machine name: {Name}", match.Name);
            return match.Id;
        }
    }

    private async Task<bool> WaitForProcessAsync(string processName, TimeSpan timeout, CancellationToken ct)
    {
        _logger.LogInfo("Waiting for process '{ProcessName}' to appear (Timeout: {Timeout}s)...", processName,
            timeout.TotalSeconds);

        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            try
            {
                var processes = PublicApi.FindProcesses(processName);
                if (processes.Count > 0)
                {
                    _logger.LogInfo("Process '{ProcessName}' found (PID: {Pid}). Proceeding.", processName,
                        processes[0].Id);
                    foreach (var p in processes)
                    {
                        p.Dispose();
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error checking for process: {Message}", ex.Message);
            }

            await Task.Delay(1000, ct);
        }

        return false;
    }

    public Task OnSessionEndAsync()
    {
        return !_authService.HasRefreshToken() ? Task.CompletedTask : _apiService.PauseAsync();
    }

    private async Task LoadDynamicChoicesAsync(bool force = false)
    {
        if (!await _choicesRefreshLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (!force && !ShouldRefreshChoices())
            {
                return;
            }

            var playlistsTask = _apiService.GetPlaylistsAsync();
            var albumsTask = _apiService.GetSavedAlbumsAsync();
            var likedSongsTask = _apiService.GetLikedSongsAsUriListAsync();

            await Task.WhenAll(playlistsTask, albumsTask, likedSongsTask);

            _cachedPlaylists = await playlistsTask;
            _cachedAlbums = await albumsTask;
            _cachedLikedSongsUri = await likedSongsTask;
            _choicesLastUpdatedUtc = DateTime.UtcNow;

            RebuildPlaybackChoices();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to refresh playlists: {Message}", ex.Message);
        }
        finally
        {
            _choicesRefreshLock.Release();
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
            {
                finalChoicesDict[playlist.Key] = playlist.Value;
            }
        }

        foreach (var album in _cachedAlbums)
        {
            if (!string.IsNullOrWhiteSpace(album.Key))
            {
                finalChoicesDict[album.Key] = album.Value;
            }
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
        _cachedPlaylists.Clear();
        _cachedAlbums.Clear();
        _cachedLikedSongsUri = string.Empty;
        _choicesLastUpdatedUtc = DateTime.MinValue;
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
        if (url.Contains("spotify:", StringComparison.Ordinal))
        {
            return url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Invalid URL format");
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/');
        return segments.Length < 2
            ? throw new ArgumentException("URL does not contain a valid Spotify type and ID")
            : $"spotify:{segments[0]}:{segments[1]}";
    }

    private void OnAuthenticationStateChanged(bool isAuthenticated)
    {
        if (isAuthenticated)
        {
            _ = RefreshChoicesAsync(force: true);
        }
        else
        {
            ClearChoices();
        }
    }

    private bool TryServeCachedChoices()
    {
        if (!ShouldUseCache())
        {
            return false;
        }

        RebuildPlaybackChoices();
        return true;
    }

    private bool ShouldUseCache()
    {
        if (_choicesLastUpdatedUtc == DateTime.MinValue)
        {
            return false;
        }

        if (DateTime.UtcNow - _choicesLastUpdatedUtc > _choicesTtl)
        {
            return false;
        }

        return _cachedPlaylists.Count > 0 || _cachedAlbums.Count > 0 ||
               !string.IsNullOrWhiteSpace(_cachedLikedSongsUri);
    }

    private bool ShouldRefreshChoices()
    {
        if (!ShouldUseCache())
        {
            return true;
        }

        return DateTime.UtcNow - _choicesLastUpdatedUtc > _choicesTtl;
    }

    private Task RefreshChoicesAsync(bool force = false)
    {
        return LoadDynamicChoicesAsync(force);
    }

    public void Dispose()
    {
        _authService.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        _disposables.Dispose();
    }
}