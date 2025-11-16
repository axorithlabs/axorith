using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.Spotify;

internal sealed class Settings
{
    internal const string CustomUrlValue = "custom";
    private const int DefaultRedirectPort = 8888;

    public Setting<string> AuthStatus { get; }
    public Setting<int> RedirectPort { get; }
    public Setting<string> TargetDevice { get; }
    public Setting<string> TargetDeviceLabel { get; }
    public Setting<string> PlaybackContext { get; }
    public Setting<string> CustomUrl { get; }
    public Setting<int> Volume { get; }
    public Setting<string> Shuffle { get; }
    public Setting<string> RepeatMode { get; }
    public Setting<double> WaitTime { get; }

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
            description: "Local HTTP port used for the Spotify OAuth callback. Change this if 8888 is blocked.");

        TargetDevice = Setting.AsChoice(
            key: "TargetDevice",
            label: "Target Device",
            defaultValue: string.Empty,
            initialChoices: Array.Empty<KeyValuePair<string, string>>(),
            description: "Device to start playback on.");

        TargetDeviceLabel = Setting.AsText(
            key: "TargetDeviceLabel",
            label: "Target Device (cached)",
            defaultValue: string.Empty,
            isVisible: false);

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

        WaitTime = Setting.AsDouble(
            key: "WaitTime",
            label: "Wait Time (ms)",
            description: "Wait Time for Spotify launch in Application Manager module or startup.",
            defaultValue: 200);

        LoginAction = Action.Create(key: "Login", label: "Login to Spotify");
        LogoutAction = Action.Create(key: "Logout", label: "Logout", isEnabled: false);

        _allSettings =
        [
            AuthStatus,
            RedirectPort,
            TargetDevice,
            TargetDeviceLabel,
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

    public Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        var deviceId = TargetDevice.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(deviceId))
            return Task.FromResult(ValidationResult.Fail("Target device must be selected"));

        return Task.FromResult(ValidationResult.Success);
    }
}