using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Axorith.Host.Models;

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

    // Internal constructor for testing with dependency injection
    internal UpdateService(ILogger<UpdateService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        CurrentVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";

#if DEBUG
        _isEnabled = false;
        _logger.LogInformation("Update service disabled in DEBUG mode");
        _initialCheckComplete.TrySetResult();
        _timer = new PeriodicTimer(TimeSpan.FromHours(24));
#else
        _isEnabled = true;

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Axorith-Host");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        }
        _httpClient.Timeout = HttpTimeout;

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

            var releaseNotes = root.TryGetProperty("body", out var bodyProp)
                ? bodyProp.GetString() ?? string.Empty
                : string.Empty;
            var publishedAt = root.GetProperty("published_at").GetDateTime();

            // Try to download and parse manifest.json from release assets
            var manifestInfo = await TryDownloadManifestAsync(root, timeoutCts.Token);

            if (manifestInfo != null)
            {
                var platform = GetCurrentPlatform();
                var arch = GetCurrentArchitecture();

                var artifact = manifestInfo.Artifacts.FirstOrDefault(a =>
                    string.Equals(a.Platform, platform, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(a.Arch, arch, StringComparison.OrdinalIgnoreCase));

                if (artifact != null)
                {
                    var updateInfo = new UpdateInfo(
                        latestVersion,
                        releaseNotes,
                        artifact.Url,
                        artifact.Sha256,
                        artifact.Platform,
                        artifact.Arch,
                        artifact.Type,
                        publishedAt);

                    LatestUpdate = updateInfo;
                    _logger.LogInformation(
                        "Update available: {LatestVersion} (current: {CurrentVersion}) via manifest",
                        latestVersion, CurrentVersion);
                    return updateInfo;
                }

                _logger.LogWarning(
                    "No artifact found for platform={Platform} arch={Arch} in manifest",
                    platform, arch);
            }

            // Fallback: parse assets directly from GitHub release (legacy, Windows-only)
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

            var updateInfoFallback = new UpdateInfo(
                latestVersion,
                releaseNotes,
                downloadUrl,
                string.Empty,
                "win",
                "x64",
                "exe",
                publishedAt);

            LatestUpdate = updateInfoFallback;

            _logger.LogInformation("Update available: {LatestVersion} (current: {CurrentVersion}) via fallback",
                latestVersion, CurrentVersion);

            return updateInfoFallback;
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

    /// <summary>
    ///     Returns update info for the latest release WITHOUT version comparison.
    ///     Used by ErrorViewModel's "Update and Restart" button even when versions are incompatible.
    /// </summary>
    public async Task<UpdateInfo?> GetUpdateInfoAsync(CancellationToken ct = default)
    {
        if (!_isEnabled)
        {
            _logger.LogDebug("GetUpdateInfo skipped (disabled in DEBUG mode)");
            return null;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(HttpTimeout);

            var response = await _httpClient.GetAsync(GitHubApiUrl, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get update info. Status: {StatusCode}", response.StatusCode);
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

            var releaseNotes = root.TryGetProperty("body", out var bodyProp)
                ? bodyProp.GetString() ?? string.Empty
                : string.Empty;
            var publishedAt = root.GetProperty("published_at").GetDateTime();

            // Try manifest first
            var manifestInfo = await TryDownloadManifestAsync(root, timeoutCts.Token);
            if (manifestInfo != null)
            {
                var platform = GetCurrentPlatform();
                var arch = GetCurrentArchitecture();
                var artifact = manifestInfo.Artifacts.FirstOrDefault(a =>
                    string.Equals(a.Platform, platform, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(a.Arch, arch, StringComparison.OrdinalIgnoreCase));

                if (artifact != null)
                {
                    return new UpdateInfo(
                        latestVersion, releaseNotes, artifact.Url, artifact.Sha256,
                        artifact.Platform, artifact.Arch, artifact.Type, publishedAt);
                }
            }

            // Fallback to direct asset parsing
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
                return null;
            }

            return new UpdateInfo(latestVersion, releaseNotes, downloadUrl, string.Empty, "win", "x64", "exe",
                publishedAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get update info");
            return null;
        }
    }

    /// <summary>
    ///     Attempts to download and parse manifest.json from release assets.
    /// </summary>
    private async Task<UpdateManifest?> TryDownloadManifestAsync(JsonElement releaseRoot, CancellationToken ct)
    {
        try
        {
            var assets = releaseRoot.GetProperty("assets");
            string? manifestUrl = null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (string.Equals(name, "manifest.json", StringComparison.OrdinalIgnoreCase))
                {
                    manifestUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            if (string.IsNullOrEmpty(manifestUrl))
            {
                _logger.LogDebug("No manifest.json found in release assets");
                return null;
            }

            var manifestJson = await _httpClient.GetStringAsync(manifestUrl, ct);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });

            if (manifest == null || manifest.Artifacts.Count == 0)
            {
                _logger.LogWarning("manifest.json is empty or has no artifacts");
                return null;
            }

            _logger.LogDebug("Parsed manifest.json with {Count} artifacts", manifest.Artifacts.Count);
            return manifest;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to download or parse manifest.json");
            return null;
        }
    }

    private static string GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "win";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "linux";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "macos";
        }

        return "unknown";
    }

    private static string GetCurrentArchitecture()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "unknown"
        };
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
