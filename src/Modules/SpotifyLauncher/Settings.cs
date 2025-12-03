using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.SpotifyLauncher;

internal sealed class Settings
{
    public Setting<string> SpotifyPath { get; }
    public Setting<string> ProcessMode { get; }
    public Setting<string> WindowState { get; }
    public Setting<bool> UseCustomSize { get; }
    public Setting<int> WindowWidth { get; }
    public Setting<int> WindowHeight { get; }
    public Setting<bool> MoveToMonitor { get; }
    public Setting<string> TargetMonitor { get; }
    public Setting<string> LifecycleMode { get; }
    public Setting<bool> BringToForeground { get; }

    public Action RefreshSpotifyAction { get; }

    private readonly IReadOnlyList<ISetting> _allSettings;
    private readonly IReadOnlyList<IAction> _allActions;
    private readonly IAppDiscoveryService _appDiscovery;

    public Settings(IAppDiscoveryService appDiscovery)
    {
        _appDiscovery = appDiscovery;

        // Changed to AsChoice to support auto-discovered list + custom entry
        SpotifyPath = Setting.AsChoice(
            key: "SpotifyPath",
            label: "Spotify Executable",
            defaultValue: string.Empty,
            initialChoices: [new KeyValuePair<string, string>("", "Scanning for Spotify...")],
            description: "Select installed Spotify or enter custom path."
        );

        ProcessMode = Setting.AsChoice(
            key: "ProcessMode",
            label: "Process Mode",
            defaultValue: "LaunchNew",
            initialChoices:
            [
                new KeyValuePair<string, string>("LaunchNew", "Launch New Process"),
                new KeyValuePair<string, string>("AttachExisting", "Attach to Existing"),
                new KeyValuePair<string, string>("LaunchOrAttach", "Launch or Attach")
            ],
            description: "How to handle the Spotify process."
        );

        WindowState = Setting.AsChoice(
            key: "WindowState",
            label: "Window State",
            defaultValue: "Normal",
            initialChoices:
            [
                new KeyValuePair<string, string>("Normal", "Normal"),
                new KeyValuePair<string, string>("Maximized", "Maximized"),
                new KeyValuePair<string, string>("Minimized", "Minimized")
            ],
            description: "Desired window state after Spotify starts."
        );

        UseCustomSize = Setting.AsCheckbox(
            key: "UseCustomSize",
            label: "Use Custom Window Size",
            defaultValue: false,
            description: "Enable custom window dimensions. Requires Normal window state."
        );

        WindowWidth = Setting.AsInt(
            key: "WindowWidth",
            label: "Window Width",
            description: "Custom window width in pixels.",
            defaultValue: 1280,
            isVisible: false
        );

        WindowHeight = Setting.AsInt(
            key: "WindowHeight",
            label: "Window Height",
            description: "Custom window height in pixels.",
            defaultValue: 720,
            isVisible: false
        );

        var monitorChoices = BuildMonitorChoices();

        MoveToMonitor = Setting.AsCheckbox(
            key: "MoveToMonitor",
            label: "Move Window To Monitor",
            defaultValue: false,
            description: "If enabled, Spotify window will be moved to the selected monitor."
        );

        TargetMonitor = Setting.AsChoice(
            key: "TargetMonitor",
            label: "Target Monitor",
            defaultValue: monitorChoices[0].Key,
            initialChoices: monitorChoices,
            description: "Monitor to move the Spotify window to after it appears.",
            isVisible: false
        );

        LifecycleMode = Setting.AsChoice(
            key: "LifecycleMode",
            label: "Process Lifecycle",
            defaultValue: "TerminateGraceful",
            initialChoices:
            [
                new KeyValuePair<string, string>("KeepRunning", "Keep Running"),
                new KeyValuePair<string, string>("TerminateGraceful", "Try Close (may remain open)"),
                new KeyValuePair<string, string>("TerminateForce", "Kill Immediately")
            ],
            description: "What happens to Spotify when session ends. 'Try Close' attempts graceful shutdown and may leave Spotify open. 'Kill Immediately' terminates Spotify without waiting."
        );

        BringToForeground = Setting.AsCheckbox(
            key: "BringToForeground",
            label: "Bring to Foreground",
            defaultValue: true,
            description: "Automatically bring the window to foreground after setup."
        );

        RefreshSpotifyAction = Action.Create("RefreshSpotify", "Refresh Spotify Path");
        RefreshSpotifyAction.OnInvokeAsync(RefreshSpotifyAsync);

        SetupReactiveVisibility();

        _allSettings =
        [
            SpotifyPath,
            ProcessMode,
            WindowState,
            UseCustomSize,
            WindowWidth,
            WindowHeight,
            MoveToMonitor,
            TargetMonitor,
            LifecycleMode,
            BringToForeground
        ];

        _allActions = [RefreshSpotifyAction];
    }

    public Task InitializeAsync()
    {
        return RefreshSpotifyAsync();
    }

    private async Task RefreshSpotifyAsync()
    {
        // Use FindKnownApp for Spotify as it's a single well-known app
        var path = await Task.Run(() => _appDiscovery.FindKnownApp("Spotify", "Spotify.exe"));

        var choices = new List<KeyValuePair<string, string>>();

        if (!string.IsNullOrEmpty(path))
        {
            choices.Add(new KeyValuePair<string, string>(path, "Spotify (Auto-Detected)"));
        }
        else
        {
            choices.Add(new KeyValuePair<string, string>("", "Spotify not found"));
        }

        // Preserve current value if it's custom (not in the list)
        var current = SpotifyPath.GetCurrentValue();
        if (!string.IsNullOrEmpty(current) && choices.All(c => c.Key != current))
        {
            choices.Insert(0, new KeyValuePair<string, string>(current, $"{current} (Custom)"));
        }

        SpotifyPath.SetChoices(choices);

        // Auto-select if current is empty and we found something
        if (string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(path))
        {
            SpotifyPath.SetValue(path);
        }
    }

    private void SetupReactiveVisibility()
    {
        UseCustomSize.Value.Subscribe(useCustom =>
        {
            var state = WindowState.GetCurrentValue();
            var isNormal = state == "Normal";
            WindowWidth.SetVisibility(isNormal && useCustom);
            WindowHeight.SetVisibility(isNormal && useCustom);
        });

        WindowState.Value.Subscribe(state =>
        {
            var isNormal = state == "Normal";
            var isMinimized = state == "Minimized";

            UseCustomSize.SetVisibility(isNormal);

            BringToForeground.SetVisibility(!isMinimized);

            var useCustom = UseCustomSize.GetCurrentValue();
            WindowWidth.SetVisibility(isNormal && useCustom);
            WindowHeight.SetVisibility(isNormal && useCustom);
        });

        MoveToMonitor.Value.Subscribe(move => { TargetMonitor.SetVisibility(move); });
    }

    private static IReadOnlyList<KeyValuePair<string, string>> BuildMonitorChoices()
    {
        var choices = new List<KeyValuePair<string, string>>();

        var monitorCount = PublicApi.GetMonitorCount();
        if (monitorCount <= 0)
        {
            monitorCount = 1;
        }

        for (var i = 0; i < monitorCount; i++)
        {
            var monitorName = PublicApi.GetMonitorName(i);
            var display = $"{i}: {monitorName}";

            choices.Add(new KeyValuePair<string, string>(i.ToString(), display));
        }

        return choices;
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
        var path = SpotifyPath.GetCurrentValue();

        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(ValidationResult.Fail("'Spotify Executable' is required."));
        }

        var mode = ProcessMode.GetCurrentValue();
        if (mode == "LaunchNew" && !File.Exists(path))
        {
            return Task.FromResult(ValidationResult.Fail($"Spotify executable not found at '{path}'."));
        }

        if (!UseCustomSize.GetCurrentValue())
        {
            return Task.FromResult(ValidationResult.Success);
        }

        if (WindowWidth.GetCurrentValue() < 100)
        {
            return Task.FromResult(ValidationResult.Fail("'Window Width' must be at least 100 pixels."));
        }

        if (WindowHeight.GetCurrentValue() < 100)
        {
            return Task.FromResult(ValidationResult.Fail("'Window Height' must be at least 100 pixels."));
        }

        return Task.FromResult(ValidationResult.Success);
    }
}