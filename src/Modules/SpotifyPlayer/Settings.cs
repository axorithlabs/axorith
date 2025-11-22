using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.SpotifyPlayer;

internal sealed class Settings
{
    internal const string CustomUrlValue = "custom";
    private const int DefaultRedirectPort = 8888;

    internal const string ModeLocalComputer = "LocalComputer";
    internal const string ModeLastActive = "LastActive";
    internal const string ModeSpecificName = "SpecificName";

    public Setting<string> AuthStatus { get; }
    public Setting<int> RedirectPort { get; }

    public Setting<bool> EnsureSpotifyRunning { get; }

    public Setting<string> DeviceSelectionMode { get; }
    public Setting<string> SpecificDeviceName { get; }

    public Setting<string> PlaybackContext { get; }
    public Setting<string> CustomUrl { get; }
    public Setting<int> Volume { get; }
    public Setting<string> Shuffle { get; }
    public Setting<string> RepeatMode { get; }

    public Action LoginAction { get; }
    public Action LogoutAction { get; }

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
            description:
            "Local HTTP port used for the Spotify OAuth callback. Change only if login fails due to port conflict.",
            isVisible: false // Hidden by default
        );

        EnsureSpotifyRunning = Setting.AsCheckbox(
            key: "EnsureSpotifyRunning",
            label: "Wait for Spotify Process",
            defaultValue: true,
            description:
            "If checked, the module will wait up to 30 seconds for the Spotify process to appear before attempting playback.");

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

        _allSettings =
        [
            AuthStatus,
            RedirectPort,
            EnsureSpotifyRunning,
            DeviceSelectionMode,
            SpecificDeviceName,
            PlaybackContext,
            CustomUrl,
            Volume,
            Shuffle,
            RepeatMode
        ];

        _allActions =
        [
            LoginAction,
            LogoutAction
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
        if (DeviceSelectionMode.GetCurrentValue() == ModeSpecificName &&
            string.IsNullOrWhiteSpace(SpecificDeviceName.GetCurrentValue()))
        {
            return Task.FromResult(
                ValidationResult.Fail("Device Name is required when 'Specific Device Name' mode is selected."));
        }

        return Task.FromResult(ValidationResult.Success);
    }
}