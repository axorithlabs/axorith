using Axorith.Sdk;
using Axorith.Sdk.Settings;
using SpotifyAPI.Web;

namespace Axorith.Module.Spotify;

public class Module : IModule
{
    private readonly ModuleDefinition _definition;
    private readonly IModuleLogger _logger;
    private SpotifyClient? _spotify = null;

    public Module(ModuleDefinition definition, IServiceProvider serviceProvider)
    {
        _definition = definition;
        _logger = (IModuleLogger)serviceProvider.GetService(typeof(IModuleLogger))!;
    }

    /// <inheritdoc />
    public IReadOnlyList<SettingBase> GetSettings()
    {
        return new List<SettingBase>
        {
            new TextSetting(
                "AccessToken",
                "Access Token",
                "",
                ""
            ),
            new TextSetting(
                "PlaylistUrl",
                "Playlist url",
                "",
                ""
            ),
        };
    }

    /// <inheritdoc />
    public Type? CustomSettingsViewType => null;

    /// <inheritdoc />
    public object? GetSettingsViewModel(IReadOnlyDictionary<string, string> currentSettings) => null;

    /// <inheritdoc />
    public Task<ValidationResult> ValidateSettingsAsync(IReadOnlyDictionary<string, string> userSettings, CancellationToken cancellationToken)
    {
        return Task.FromResult(ValidationResult.Success);
    }

    /// <inheritdoc />
    public async Task OnSessionStartAsync(IReadOnlyDictionary<string, string> userSettings, CancellationToken cancellationToken)
    {
        _spotify ??= new SpotifyClient(userSettings.GetValueOrDefault("AccessToken", string.Empty));
        
        var playlistUrl = userSettings.GetValueOrDefault("PlaylistUrl", string.Empty);

        try
        {
            if (string.IsNullOrEmpty(playlistUrl))
                await _spotify.Player.AddToQueue(new PlayerAddToQueueRequest(playlistUrl), cancellationToken);
            await _spotify.Player.SkipNext(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Exception {Exception}", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task OnSessionEndAsync()
    {
        await _spotify?.Player.PausePlayback()!;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
