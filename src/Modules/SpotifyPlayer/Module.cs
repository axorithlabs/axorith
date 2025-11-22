using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Http;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;

namespace Axorith.Module.SpotifyPlayer;

public class Module : IModule
{
    private readonly Settings _settings;
    private readonly AuthService _authService;
    private readonly SpotifyApiService _apiService;
    private readonly PlaybackService _playbackService;

    public Module(
        IModuleLogger logger,
        IHttpClientFactory httpClientFactory,
        ISecureStorageService secureStorage,
        IAppDiscoveryService appDiscoveryService,
        ModuleDefinition definition)
    {
        _settings = new Settings();

        _authService = new AuthService(logger, httpClientFactory, secureStorage, definition, _settings);
        _apiService = new SpotifyApiService(httpClientFactory, definition, _authService, logger);
        _playbackService = new PlaybackService(logger, _settings, _authService, _apiService);
    }

    public IReadOnlyList<ISetting> GetSettings()
    {
        return _settings.GetSettings();
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return _settings.GetActions();
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return _playbackService.InitializeAsync();
    }

    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        return _settings.ValidateAsync();
    }

    public Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        return _playbackService.OnSessionStartAsync(cancellationToken);
    }

    public Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        return _playbackService.OnSessionEndAsync();
    }

    public void Dispose()
    {
        _playbackService.Dispose();
        _authService.Dispose();
    }
}