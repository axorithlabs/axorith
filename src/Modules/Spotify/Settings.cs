using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.Spotify;

internal sealed class Settings : LauncherSettingsBase
{
    internal const string CustomUrlValue = "custom";

    internal const string ModeLocalComputer = "LocalComputer";
    internal const string ModeLastActive = "LastActive";
    internal const string ModeSpecificName = "SpecificName";

    private readonly IAppDiscoveryService _appDiscovery;

    public override Setting<string> ApplicationPath => SpotifyPath;
    public Setting<string> SpotifyPath { get; }
    public Action RefreshSpotifyAction { get; }

    public Setting<string> AuthStatus { get; }
    public Setting<bool> EnablePlayback { get; }
    public Setting<string> DeviceSelectionMode { get; }
    public Setting<string> SpecificDeviceName { get; }
    public Setting<string> PlaybackContext { get; }
    public Setting<string> CustomUrl { get; }
    public Setting<int> Volume { get; }
    public Setting<string> Shuffle { get; }
    public Setting<string> RepeatMode { get; }

    public Action LoginAction { get; }
    public Action LogoutAction { get; }

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

        EnablePlayback = Setting.AsCheckbox(
            key: "EnablePlayback",
            label: "Enable Playback Control",
            defaultValue: true,
            description: "When enabled, the module will control Spotify playback on session start/end."
        );

        AuthStatus = Setting.AsText(
            key: "AuthStatus",
            label: "Authentication",
            defaultValue: "Not authenticated",
            isReadOnly: true);

        DeviceSelectionMode = Setting.AsChoice(
            key: "DeviceSelectionMode",
            label: "Target Device",
            defaultValue: ModeLocalComputer,
            initialChoices:
            [
                new KeyValuePair<string, string>(ModeLocalComputer, "Local Computer (Recommended)"),
                new KeyValuePair<string, string>(ModeLastActive, "Most Recently Active Device"),
                new KeyValuePair<string, string>(ModeSpecificName, "Specific Device Name (Advanced)")
            ],
            description: "How to select the device for playback."
        );

        SpecificDeviceName = Setting.AsText(
            key: "SpecificDeviceName",
            label: "Device Name",
            defaultValue: "",
            description: "The exact name of the device to control (e.g. 'Living Room Speaker').",
            isVisible: false
        );

        PlaybackContext = Setting.AsChoice(
            key: "PlaybackContext",
            label: "Playback Source",
            defaultValue: CustomUrlValue,
            initialChoices:
            [
                new KeyValuePair<string, string>(CustomUrlValue, "Enter a custom URL...")
            ],
            description: "Select a source or enter a custom URL.");

        CustomUrl = Setting.AsText(
            key: "CustomUrl",
            label: "Custom URL",
            defaultValue: string.Empty,
            description: "URL of the track, playlist, or album to play.",
            isVisible: false);

        Volume = Setting.AsInt(
            key: "Volume",
            label: "Volume",
            defaultValue: 80,
            description: "Playback volume (0-100).");

        Shuffle = Setting.AsChoice(
            key: "Shuffle",
            label: "Shuffle Mode",
            defaultValue: "false",
            initialChoices:
            [
                new KeyValuePair<string, string>("true", "On"),
                new KeyValuePair<string, string>("false", "Off")
            ]);

        RepeatMode = Setting.AsChoice(
            key: "RepeatMode",
            label: "Repeat Mode",
            defaultValue: "off",
            initialChoices:
            [
                new KeyValuePair<string, string>("off", "Off"),
                new KeyValuePair<string, string>("context", "Repeat Playlist/Album"),
                new KeyValuePair<string, string>("track", "Repeat Track")
            ]);

        LoginAction = Action.Create(key: "Login", label: "Login to Spotify");
        LogoutAction = Action.Create(key: "Logout", label: "Logout", isEnabled: false);

        DeviceSelectionMode.Value.Subscribe(mode => { SpecificDeviceName.SetVisibility(mode == ModeSpecificName); });

        EnablePlayback.Value.Subscribe(enabled =>
        {
            AuthStatus.SetVisibility(enabled);
            DeviceSelectionMode.SetVisibility(enabled);
            PlaybackContext.SetVisibility(enabled);
            Volume.SetVisibility(enabled);
            Shuffle.SetVisibility(enabled);
            RepeatMode.SetVisibility(enabled);
        });

        SetupBaseReactiveVisibility();
    }

    protected override IEnumerable<ISetting> GetAdditionalSettings()
    {
        yield return EnablePlayback;
        yield return AuthStatus;
        yield return DeviceSelectionMode;
        yield return SpecificDeviceName;
        yield return PlaybackContext;
        yield return CustomUrl;
        yield return Volume;
        yield return Shuffle;
        yield return RepeatMode;
    }

    protected override IEnumerable<IAction> GetAdditionalActions()
    {
        yield return RefreshSpotifyAction;
        yield return LoginAction;
        yield return LogoutAction;
    }

    protected override Task InitializeAdditionalAsync()
    {
        return RefreshSpotifyAsync();
    }

    public new Task<ValidationResult> ValidateAsync()
    {
        if (DeviceSelectionMode.GetCurrentValue() == ModeSpecificName &&
            string.IsNullOrWhiteSpace(SpecificDeviceName.GetCurrentValue()))
        {
            return Task.FromResult(
                ValidationResult.Fail("Device Name is required when 'Specific Device Name' mode is selected."));
        }

        if (EnablePlayback.GetCurrentValue() &&
            PlaybackContext.GetCurrentValue() == CustomUrlValue &&
            string.IsNullOrWhiteSpace(CustomUrl.GetCurrentValue()))
        {
            return Task.FromResult(
                ValidationResult.Fail(
                    new Dictionary<string, string>
                        { [CustomUrl.Key] = "Custom URL is required when 'Enter a custom URL' is selected." },
                    "Please enter a Spotify URL (track, playlist, or album)."));
        }

        return base.ValidateAsync();
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

    public override void Dispose()
    {
        SpotifyPath.Dispose();
        RefreshSpotifyAction.Dispose();
        AuthStatus.Dispose();
        EnablePlayback.Dispose();
        DeviceSelectionMode.Dispose();
        SpecificDeviceName.Dispose();
        PlaybackContext.Dispose();
        CustomUrl.Dispose();
        Volume.Dispose();
        Shuffle.Dispose();
        RepeatMode.Dispose();
        LoginAction.Dispose();
        LogoutAction.Dispose();
        base.Dispose();
    }
}