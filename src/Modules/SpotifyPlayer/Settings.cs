using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.SpotifyPlayer;

internal sealed class Settings
{
    internal const string CUSTOM_URL_VALUE = "custom";
    private const int DefaultRedirectPort = 8888;

    public Setting<string> AuthStatus { get; }
    public Setting<int> RedirectPort { get; }
    public Setting<string> TargetDevice { get; }
    public Setting<string> PlaybackContext { get; }
    public Setting<string> CustomUrl { get; }
    public Setting<int> Volume { get; }
    public Setting<string> Shuffle { get; }
    public Setting<string> RepeatMode { get; }
    public Setting<double> WaitTime { get; }

    public Action LoginAction { get; }
    public Action LogoutAction { get; }
    public Action UpdateAction { get; }

    private readonly IReadOnlyList<ISetting> _allSettings;
    private readonly IReadOnlyList<IAction> _allActions;

    public Settings()
    {
        AuthStatus = Setting.AsText(
            key: "AuthStatus",
            label: "Authentication",
            defaultValue: string.Empty,
            isReadOnly: true);

        RedirectPort = Setting.AsInt(
            key: "RedirectPort",
            label: "Redirect Port",
            defaultValue: DefaultRedirectPort,
            description: "Local HTTP port used for the Spotify OAuth callback. Change this if 8888 is blocked.");

        TargetDevice = Setting.AsChoice(
            key: "TargetDevice",
            label: "Target Device",
            defaultValue: string.Empty,
            initialChoices: [],
            description: "Device to start playback on.");

        PlaybackContext = Setting.AsChoice(
            key: "PlaybackContext",
            label: "Playback Source",
            defaultValue: CUSTOM_URL_VALUE,
            initialChoices:
            [
                new KeyValuePair<string, string>(CUSTOM_URL_VALUE, "Enter a custom URL...")
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

        WaitTime = Setting.AsDouble(
            key: "WaitTime",
            label: "Wait Time (ms)",
            description: "Wait Time for Spotify launch in Application Manager module or startup.",
            defaultValue: 200);

        LoginAction = Action.Create(key: "Login", label: "Login to Spotify");
        LogoutAction = Action.Create(key: "Logout", label: "Logout", isEnabled: false);
        UpdateAction = Action.Create(key: "Update", label: "Update devices and playlists");

        _allSettings =
        [
            AuthStatus,
            RedirectPort,
            TargetDevice,
            PlaybackContext,
            CustomUrl,
            Volume,
            Shuffle,
            RepeatMode,
            WaitTime
        ];

        _allActions =
        [
            LoginAction,
            LogoutAction,
            UpdateAction
        ];
    }

    public IReadOnlyList<ISetting> GetSettings()
    {
        return _allSettings;
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return _allActions;
    }

    public Task<ValidationResult> ValidateAsync()
    {
        return Task.FromResult(ValidationResult.Success);
    }
}