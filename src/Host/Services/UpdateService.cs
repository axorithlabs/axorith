using System.Reflection;
using System.Text.Json;

namespace Axorith.Host.Services;

public class UpdateService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateService> _logger;
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private const string GitHubApiUrl = "https://api.github.com/repos/axorithlabs/axorith/releases/latest";

    public string CurrentVersion { get; }
    public UpdateInfo? LatestUpdate { get; private set; }
    public bool UpdateAvailable => LatestUpdate != null;

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Axorith-Host");

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        CurrentVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";

        _timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        _ = Task.Run(CheckForUpdatesLoop);
    }

    private async Task CheckForUpdatesLoop()
    {
        await CheckForUpdatesAsync(_cts.Token);

        try
        {
            while (await _timer.WaitForNextTickAsync(_cts.Token))
            {
                await CheckForUpdatesAsync(_cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Checking for updates. Current version: {CurrentVersion}", CurrentVersion);

            var response = await _httpClient.GetAsync(GitHubApiUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to check for updates. Status: {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString();
            if (string.IsNullOrEmpty(tagName))
            {
                return null;
            }

            var latestVersion = tagName.TrimStart('v');

            if (!IsNewerVersion(latestVersion, CurrentVersion))
            {
                _logger.LogInformation("No updates available. Latest: {LatestVersion}", latestVersion);
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
                _logger.LogWarning("No installer found in release assets");
                return null;
            }

            var releaseNotes = root.GetProperty("body").GetString() ?? string.Empty;
            var publishedAt = root.GetProperty("published_at").GetDateTime();

            var updateInfo = new UpdateInfo(latestVersion, downloadUrl, releaseNotes, publishedAt);
            LatestUpdate = updateInfo;

            _logger.LogInformation("Update available: {LatestVersion}", latestVersion);

            return updateInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for updates");
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