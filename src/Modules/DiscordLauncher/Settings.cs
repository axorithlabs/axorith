using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;
using Axorith.Shared.Utils;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.DiscordLauncher;

internal sealed class Settings
{
    public Setting<string> DiscordPath { get; }
    public Setting<string> ProcessMode { get; }
    public Setting<string> WindowState { get; }
    public Setting<bool> UseCustomSize { get; }
    public Setting<int> WindowWidth { get; }
    public Setting<int> WindowHeight { get; }
    public Setting<bool> MoveToMonitor { get; }
    public Setting<string> TargetMonitor { get; }
    public Setting<string> LifecycleMode { get; }
    public Setting<bool> BringToForeground { get; }

    public Action RefreshPathAction { get; }

    private readonly IReadOnlyList<ISetting> _allSettings;
    private readonly IReadOnlyList<IAction> _allActions;
    private readonly IAppDiscoveryService _appDiscovery;

    public Settings(IAppDiscoveryService appDiscovery)
    {
        _appDiscovery = appDiscovery;

        DiscordPath = Setting.AsChoice(
            key: "DiscordPath",
            label: "Discord Executable",
            defaultValue: string.Empty,
            initialChoices: [new KeyValuePair<string, string>("", "Scanning for Discord...")],
            description: "Path to Discord executable (Update.exe or Discord.exe)."
        );

        ProcessMode = Setting.AsChoice(
            key: "ProcessMode",
            label: "Process Mode",
            defaultValue: "LaunchNew",
            initialChoices:
            [
                new KeyValuePair<string, string>("LaunchNew", "Launch New Window"),
                new KeyValuePair<string, string>("AttachExisting", "Attach to Existing"),
                new KeyValuePair<string, string>("LaunchOrAttach", "Launch or Attach")
            ],
            description: "How to handle the Discord process."
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
            description: "Desired window state."
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
            description: "If enabled, window will be moved to the selected monitor."
        );

        TargetMonitor = Setting.AsChoice(
            key: "TargetMonitor",
            label: "Target Monitor",
            defaultValue: monitorChoices[0].Key,
            initialChoices: monitorChoices,
            description: "Monitor to move the window to.",
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
            description: "What happens to Discord when session ends. 'Try Close' attempts graceful shutdown and may leave Discord open. 'Kill Immediately' terminates Discord without waiting."
        );

        BringToForeground = Setting.AsCheckbox(
            key: "BringToForeground",
            label: "Bring to Foreground",
            defaultValue: true,
            description: "Automatically bring the window to foreground."
        );

        RefreshPathAction = Action.Create("RefreshPath", "Refresh Path");
        RefreshPathAction.OnInvokeAsync(RefreshPathAsync);

        SetupReactiveVisibility();

        _allSettings =
        [
            DiscordPath,
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

        _allActions = [RefreshPathAction];
    }

    public Task InitializeAsync()
    {
        return RefreshPathAsync();
    }

    private async Task RefreshPathAsync()
    {
        var platform = EnvironmentUtils.GetCurrentPlatform();
        
        string? path = null;

        if (platform == Platform.Windows)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var discordBase = Path.Combine(localAppData, "Discord");

            if (Directory.Exists(discordBase))
            {
                var appFolders = Directory.GetDirectories(discordBase, "app-*")
                    .OrderByDescending(d => d)
                    .ToList();

                foreach (var appFolder in appFolders)
                {
                    var discordExe = Path.Combine(appFolder, "Discord.exe");
                    if (File.Exists(discordExe))
                    {
                        path = discordExe;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(path))
            {
                path = await Task.Run(() => _appDiscovery.FindKnownApp("Discord.exe", "Discord"));
            }
        }
        else
        {
            path = await Task.Run(() => _appDiscovery.FindKnownApp("discord"));
        }

        var choices = new List<KeyValuePair<string, string>>
        {
            !string.IsNullOrEmpty(path)
                ? new KeyValuePair<string, string>(path, "Discord (Auto-Detected)")
                : new KeyValuePair<string, string>("", "Discord not found")
        };

        var current = DiscordPath.GetCurrentValue();
        if (!string.IsNullOrEmpty(current) && choices.All(c => c.Key != current))
        {
            choices.Insert(0, new KeyValuePair<string, string>(current, $"{current} (Custom)"));
        }

        DiscordPath.SetChoices(choices);

        if (string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(path))
        {
            DiscordPath.SetValue(path);
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

        ProcessMode.Value.Subscribe(_ =>
        {
            // Discord doesn't use custom args
        });
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
            choices.Add(new KeyValuePair<string, string>(i.ToString(), $"{i}: {monitorName}"));
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
        var path = DiscordPath.GetCurrentValue();

        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(ValidationResult.Fail("'Discord Executable' is required."));
        }

        var mode = ProcessMode.GetCurrentValue();
        if (mode == "LaunchNew" && !File.Exists(path))
        {
            return Task.FromResult(ValidationResult.Fail($"Executable not found at '{path}'."));
        }

        if (UseCustomSize.GetCurrentValue())
        {
            if (WindowWidth.GetCurrentValue() < 100)
            {
                return Task.FromResult(ValidationResult.Fail("'Window Width' must be at least 100 pixels."));
            }

            if (WindowHeight.GetCurrentValue() < 100)
            {
                return Task.FromResult(ValidationResult.Fail("'Window Height' must be at least 100 pixels."));
            }
        }

        return Task.FromResult(ValidationResult.Success);
    }
}