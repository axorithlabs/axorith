using System.Reflection;
using System.Net;
using System.Text.Json;
using Axorith.Shared.Utils;

namespace Axorith.Host.Services;

public sealed class UpdateService : BackgroundService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateService> _logger;
    private readonly bool _isEnabled;
    private readonly object _checkLock = new();
    private readonly TaskCompletionSource _initialCheckComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task<UpdateInfo?>? _pendingCheck;
    private CancellationToken _stoppingToken;
    private volatile UpdateInfo? _latestUpdate;
    private volatile string? _lastCheckError;
    private TimeSpan _retryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(15);

    public string CurrentVersion { get; }
    public UpdateInfo? LatestUpdate => _latestUpdate;
    public bool UpdateAvailable => LatestUpdate is not null;
    public string? LastCheckError => _lastCheckError;

    public UpdateService(ILogger<UpdateService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        CurrentVersion = version?.ToString(3) ?? "0.0.0";
        _httpClient = httpClientFactory.CreateClient("updates");
        _httpClient.Timeout = HttpTimeout;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Axorith-Host");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

        #if DEBUG
        _isEnabled = false;
        #else
        _isEnabled = Environment.GetEnvironmentVariable("AXORITH_TEST_MODE") != "1";
        #endif

        if (!_isEnabled)
        {
            _logger.LogInformation("Update service disabled for this build or test run");
            _initialCheckComplete.TrySetResult();
        }
    }

    public Task WaitForInitialCheckAsync(CancellationToken ct = default) => _initialCheckComplete.Task.WaitAsync(ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        if (!_isEnabled) return;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckForUpdatesAsync(stoppingToken);
                }
                finally
                {
                    _initialCheckComplete.TrySetResult();
                }

                await Task.Delay(LastCheckError is null ? TimeSpan.FromMinutes(5) : _retryDelay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _initialCheckComplete.TrySetCanceled(stoppingToken);
        }
    }

    public Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        if (!_isEnabled) return Task.FromResult<UpdateInfo?>(null);

        Task<UpdateInfo?> check;
        lock (_checkLock)
        {
            if (_pendingCheck is null || _pendingCheck.IsCompleted)
                _pendingCheck = CheckForUpdatesCoreAsync(_stoppingToken);
            check = _pendingCheck;
        }

        return check.WaitAsync(ct);
    }

    private async Task<UpdateInfo?> CheckForUpdatesCoreAsync(CancellationToken ct)
    {
        _logger.LogInformation("Checking for updates. Current version: {CurrentVersion}", CurrentVersion);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(HttpTimeout);
            using var response = await _httpClient.GetAsync(UpdateInstallerSource.LatestReleaseUrl, timeout.Token);
            _retryDelay = TimeSpan.FromSeconds(30);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromHours(1);
                _retryDelay = TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 30, 3600));
            }
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("Release tag is missing.");
            var latestVersion = tag.StartsWith('v') ? tag[1..] : tag;
            UpdateInstallerSource.GetDownloadUri(latestVersion);

            if ((root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) ||
                (root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean()))
                throw new InvalidDataException("The update server returned an unpublished or prerelease version.");

            if (Version.Parse(latestVersion) <= Version.Parse(CurrentVersion))
            {
                _latestUpdate = null;
                _lastCheckError = null;
                _logger.LogInformation("No updates available. Current: {CurrentVersion}, Latest: {LatestVersion}", CurrentVersion, latestVersion);
                return null;
            }

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                if (!string.Equals(asset.GetProperty("name").GetString(), "axorith-setup.exe", StringComparison.Ordinal)) continue;
                var url = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                UpdateInstallerSource.ValidateDownloadUrl(latestVersion, url);
                var digest = asset.TryGetProperty("digest", out var digestProperty) ? digestProperty.GetString() : null;
                var hash = UpdateInstallerSource.NormalizeSha256(digest);
                var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty;
                var update = new UpdateInfo(latestVersion, url, notes, root.GetProperty("published_at").GetDateTime(), hash);
                _latestUpdate = update;
                _lastCheckError = null;
                _logger.LogInformation("Update available: {LatestVersion} (current: {CurrentVersion})", latestVersion, CurrentVersion);
                return update;
            }

            throw new InvalidDataException("The release has no Axorith installer.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _lastCheckError = "The update check timed out. Check your connection and try again.";
            _logger.LogWarning("Update check timed out after {Timeout}s", HttpTimeout.TotalSeconds);
        }
        catch (HttpRequestException ex)
        {
            _lastCheckError = "Could not reach the update server. Check your connection and try again.";
            _logger.LogWarning(ex, "Network error while checking for updates");
        }
        catch (Exception ex)
        {
            _lastCheckError = "Could not read the update release. Please try again later.";
            _logger.LogWarning(ex, "Invalid update release metadata");
        }

        return LatestUpdate;
    }

    public override void Dispose()
    {
        base.Dispose();
        _httpClient.Dispose();
    }
}

public record UpdateInfo(string Version, string DownloadUrl, string ReleaseNotes, DateTime PublishedAt, string Sha256Hash);
