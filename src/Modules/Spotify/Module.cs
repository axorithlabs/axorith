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
                "Playlist URL",
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
                await _spotify.Player.AddToQueue(new PlayerAddToQueueRequest(ConvertUrlToUri(playlistUrl)), cancellationToken);
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
    
    /// <summary>
    /// Converts a Spotify URL to a Spotify URI.
    /// Example: 
    /// Input: "https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M?si=abc123"
    /// Output: "spotify:playlist:37i9dQZF1DXcBWIGoYBM5M"
    /// </summary>
    /// <param name="url">Spotify URL</param>
    /// <returns>Spotify URI</returns>
    private static string ConvertUrlToUri(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be empty");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("Invalid URL format");

        // URL path: /playlist/37i9dQZF1DXcBWIGoYBM5M
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2)
            throw new ArgumentException("URL does not contain a valid Spotify type and ID");

        var type = segments[0]; // playlist, track, album, etc.
        var id = segments[1];

        return $"spotify:{type}:{id}";
    }
}
