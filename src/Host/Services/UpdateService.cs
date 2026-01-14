using System.Reflection;
using System.Text.Json;

namespace Axorith.Host.Services;

public class UpdateService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateService> _logger;
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();

    private readonly TaskCompletionSource _initialCheckComplete =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly bool _isEnabled;
    private const string GitHubApiUrl = "https://api.github.com/repos/axorithlabs/axorith/releases/latest";
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(15);

    public string CurrentVersion { get; }
    public UpdateInfo? LatestUpdate { get; private set; }
    public bool UpdateAvailable => LatestUpdate != null;

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        CurrentVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";

        #if DEBUG
        _isEnabled = false;
        _logger.LogInformation("Update service disabled in DEBUG mode");
        _initialCheckComplete.TrySetResult();
        _timer = new PeriodicTimer(TimeSpan.FromHours(24));
        _httpClient = new HttpClient();
        #else
        _isEnabled = true;

        _httpClient = new HttpClient
        {
            Timeout = HttpTimeout
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Axorith-Host");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

        _timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        _ = Task.Run(CheckForUpdatesLoopAsync);
        #endif
    }

    /// <summary>
    ///     Waits for the initial update check to complete (with timeout).
    /// </summary>
    public async Task WaitForInitialCheckAsync(CancellationToken ct = default)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await _initialCheckComplete.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Initial update check timed out or was cancelled");
        }
    }

    private async Task CheckForUpdatesLoopAsync()
    {
        try
        {
            await CheckForUpdatesAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Initial update check failed");
        }
        finally
        {
            _initialCheckComplete.TrySetResult();
        }

        try
        {
            while (await _timer.WaitForNextTickAsync(_cts.Token))
            {
                try
                {
                    await CheckForUpdatesAsync(_cts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Periodic update check failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        if (!_isEnabled)
        {
            _logger.LogDebug("Update check skipped (disabled in DEBUG mode)");
            return null;
        }

        _logger.LogInformation("Checking for updates. Current version: {CurrentVersion}", CurrentVersion);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(HttpTimeout);

            var response = await _httpClient.GetAsync(GitHubApiUrl, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to check for updates. Status: {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString();
            if (string.IsNullOrEmpty(tagName))
            {
                _logger.LogWarning("GitHub release has no tag_name");
                return null;
            }

            var latestVersion = tagName.TrimStart('v');

            if (!IsNewerVersion(latestVersion, CurrentVersion))
            {
                _logger.LogInformation("No updates available. Current: {CurrentVersion}, Latest: {LatestVersion}",
                    CurrentVersion, latestVersion);
                LatestUpdate = null;
                return null;
            }

            var assets = root.GetProperty("assets");
            string? downloadUrl = null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
            {
                _logger.LogWarning("No installer found in release assets for version {Version}", latestVersion);
                return null;
            }

            var releaseNotes = root.TryGetProperty("body", out var bodyProp)
                ? bodyProp.GetString() ?? string.Empty
                : string.Empty;
            var publishedAt = root.GetProperty("published_at").GetDateTime();

            var updateInfo = new UpdateInfo(latestVersion, downloadUrl, releaseNotes, publishedAt);
            LatestUpdate = updateInfo;

            _logger.LogInformation("Update available: {LatestVersion} (current: {CurrentVersion})",
                latestVersion, CurrentVersion);

            return updateInfo;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Update check cancelled");
            throw;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Update check timed out after {Timeout}s", HttpTimeout.TotalSeconds);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error while checking for updates");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse GitHub API response");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error checking for updates");
            return null;
        }
    }

    private static bool IsNewerVersion(string latestVersion, string currentVersion)
    {
        try
        {
            var latest = ParseVersion(latestVersion);
            var current = ParseVersion(currentVersion);

            return latest > current;
        }
        catch
        {
            return false;
        }
    }

    private static Version ParseVersion(string version)
    {
        var versionString = version.Split('-')[0];
        return Version.Parse(versionString);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _timer.Dispose();
        _httpClient.Dispose();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}

public record UpdateInfo(string Version, string DownloadUrl, string ReleaseNotes, DateTime PublishedAt);