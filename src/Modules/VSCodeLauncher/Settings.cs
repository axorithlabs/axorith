using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;
using Axorith.Shared.Utils;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.VSCodeLauncher;

internal sealed class Settings
{
    public Setting<string> CodePath { get; }
    public Setting<string> ProjectPath { get; }
    public Setting<string> ProcessMode { get; }
    public Setting<string> ApplicationArgs { get; }
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

        CodePath = Setting.AsChoice(
            key: "CodePath",
            label: "VS Code Executable",
            defaultValue: string.Empty,
            initialChoices: [new KeyValuePair<string, string>("", "Scanning for VS Code...")],
            description: "Path to Visual Studio Code executable."
        );

        ProjectPath = Setting.AsDirectoryPicker(
            key: "ProjectPath",
            label: "Project Path",
            defaultValue: Environment.CurrentDirectory,
            description: "Path to the folder or workspace to open."
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
            description: "How to handle the VS Code process."
        );

        ApplicationArgs = Setting.AsText(
            key: "ApplicationArgs",
            label: "Launch Arguments",
            description: "Additional command-line arguments (e.g. --disable-extensions).",
            defaultValue: ""
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
            description: "What happens to VS Code when session ends. 'Try Close' attempts graceful shutdown and may leave VS Code open. 'Kill Immediately' terminates VS Code without waiting."
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
            CodePath,
            ProjectPath,
            ProcessMode,
            ApplicationArgs,
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
        var exeName = platform == Platform.Windows ? "Code.exe" : "code";

        var path = await Task.Run(() => _appDiscovery.FindKnownApp(exeName, "Visual Studio Code", "Code"));

        var choices = new List<KeyValuePair<string, string>>
        {
            !string.IsNullOrEmpty(path)
                ? new KeyValuePair<string, string>(path, "Visual Studio Code (Auto-Detected)")
                : new KeyValuePair<string, string>("", "VS Code not found")
        };

        var current = CodePath.GetCurrentValue();
        if (!string.IsNullOrEmpty(current) && choices.All(c => c.Key != current))
        {
            choices.Insert(0, new KeyValuePair<string, string>(current, $"{current} (Custom)"));
        }

        CodePath.SetChoices(choices);

        if (string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(path))
        {
            CodePath.SetValue(path);
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

        ProcessMode.Value.Subscribe(mode =>
        {
            var showArgs = mode is "LaunchNew" or "LaunchOrAttach";
            ApplicationArgs.SetVisibility(showArgs);
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
        var path = CodePath.GetCurrentValue();

        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(ValidationResult.Fail("'VS Code Executable' is required."));
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