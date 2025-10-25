using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Axorith.Sdk;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Settings;

namespace Axorith.Module.Spotify;

public class Module : IModule
{
    private readonly IModuleLogger _logger;
    private readonly HttpClient _httpClient;

    public Module(ModuleDefinition definition, IServiceProvider serviceProvider)
    {
        _logger = (IModuleLogger)serviceProvider.GetService(typeof(IModuleLogger))!;
        _httpClient = new HttpClient();
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
        _httpClient.DefaultRequestHeaders.Authorization ??= new AuthenticationHeaderValue("Bearer", userSettings.GetValueOrDefault("AccessToken", string.Empty));
        
        var playlistUrl = userSettings.GetValueOrDefault("PlaylistUrl", string.Empty);

        try
        {
            if (string.IsNullOrEmpty(playlistUrl))
                await PlayPlaylistAsync(userSettings.GetValueOrDefault("PlaylistUrl", string.Empty), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Exception {Exception}", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task OnSessionEndAsync()
    {
        await StopPlaybackAsync();
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

    /// <summary>
    /// Starts playback of a Spotify playlist given its URL
    /// </summary>
    /// <param name="playlistUrl">Spotify playlist URL</param>
    /// <param name="cancellationToken">CancellationToken</param>
    private async Task PlayPlaylistAsync(string playlistUrl, CancellationToken cancellationToken)
    {
        var playlistUri = ConvertUrlToUri(playlistUrl);

        var content = new StringContent(JsonSerializer.Serialize(new { context_uri = playlistUri }), Encoding.UTF8, "application/json");

        var response = await _httpClient.PutAsync("https://api.spotify.com/v1/me/player/play", content, cancellationToken);

        if (response.IsSuccessStatusCode)
            Console.WriteLine("Playlist started successfully.");
        else
        {
            var resp = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"Error playing playlist: {resp}");
        }
    }

    /// <summary>
    /// Stops current playback
    /// </summary>
    private async Task StopPlaybackAsync()
    {
        var response = await _httpClient.PutAsync("https://api.spotify.com/v1/me/player/pause", null);

        if (response.IsSuccessStatusCode)
            Console.WriteLine("Playback stopped successfully.");
        else
        {
            var resp = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Error stopping playback: {resp}");
        }
    }

}
