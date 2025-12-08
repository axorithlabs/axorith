using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.SpotifyLauncher;

internal sealed class Settings : LauncherSettingsBase
{
    public override Setting<string> ApplicationPath => SpotifyPath;

    public Setting<string> SpotifyPath { get; }

    public Action RefreshSpotifyAction { get; }

    private readonly IAppDiscoveryService _appDiscovery;

    public Settings(IAppDiscoveryService appDiscovery)
    {
        _appDiscovery = appDiscovery;

        SpotifyPath = Setting.AsChoice(
            key: "SpotifyPath",
            label: "Spotify Executable",
            defaultValue: string.Empty,
            initialChoices: [new KeyValuePair<string, string>("", "Scanning for Spotify...")],
            description: "Select installed Spotify or enter custom path."
        );

        RefreshSpotifyAction = Action.Create("RefreshSpotify", "Refresh Spotify Path");
        RefreshSpotifyAction.OnInvokeAsync(RefreshSpotifyAsync);

        // Setup reactive visibility after all fields are initialized
        SetupBaseReactiveVisibility();
    }

    protected override IEnumerable<ISetting> GetAdditionalSettings()
    {
        yield break; // No additional settings for Spotify
    }

    protected override IEnumerable<IAction> GetAdditionalActions()
    {
        yield return RefreshSpotifyAction;
    }

    protected override Task InitializeAdditionalAsync()
    {
        return RefreshSpotifyAsync();
    }

    private async Task RefreshSpotifyAsync()
    {
        var path = await Task.Run(() => _appDiscovery.FindKnownApp("Spotify", "Spotify.exe")).ConfigureAwait(false);

        var choices = new List<KeyValuePair<string, string>>
        {
            !string.IsNullOrEmpty(path)
                ? new KeyValuePair<string, string>(path, "Spotify (Auto-Detected)")
                : new KeyValuePair<string, string>("", "Spotify not found")
        };

        var current = SpotifyPath.GetCurrentValue();
        if (!string.IsNullOrEmpty(current) && choices.All(c => c.Key != current))
        {
            choices.Insert(0, new KeyValuePair<string, string>(current, $"{current} (Custom)"));
        }

        SpotifyPath.SetChoices(choices);

        if (string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(path))
        {
            SpotifyPath.SetValue(path);
        }
    }
}