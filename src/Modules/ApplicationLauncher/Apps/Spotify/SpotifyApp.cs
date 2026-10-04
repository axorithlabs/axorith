using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.ApplicationLauncher.Apps.Spotify;

internal sealed class SpotifyApp : LauncherAppBase
{
    private readonly Settings _settings;
    private readonly AuthService _authService;
    private readonly PlaybackService _playbackService;
    private bool _disposed;

    public SpotifyApp(
        IModuleLogger logger,
        IHttpClientFactory httpClientFactory,
        ISecureStorageService secureStorage,
        INotifier notifier,
        IAppDiscoveryService appDiscovery,
        IPlatformProcessService processService,
        IPlatformWindowService windowService)
        : base(logger, processService, windowService)
    {
        _settings = new Settings(appDiscovery);
        _authService = new AuthService(logger, httpClientFactory, secureStorage, _settings, notifier);
        var apiService = new SpotifyApiService(httpClientFactory, _authService, logger);
        _playbackService = new PlaybackService(logger, _settings, _authService, apiService, processService);
    }

    protected override LauncherSettingsBase Settings => _settings;
    protected override WindowConfigTimings GetWindowConfigTimings() => new(5_000, 1_000, 1_000, 1_000);

    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await base.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _playbackService.InitializeAsync().ConfigureAwait(false);
    }

    public override async Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        await base.OnSessionStartAsync(cancellationToken).ConfigureAwait(false);
        await _playbackService.OnSessionStartAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        await _playbackService.OnSessionEndAsync().ConfigureAwait(false);
        await base.OnSessionEndAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _playbackService.Dispose();
        _authService.Dispose();
        base.Dispose();
    }
}
