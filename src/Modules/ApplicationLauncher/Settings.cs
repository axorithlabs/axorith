using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.ApplicationLauncher;

internal sealed class Settings
{
    public Setting<string> ApplicationPath { get; }
    public Setting<string> ApplicationArgs { get; }
    public Setting<string> ProcessMode { get; }
    public Setting<bool> UseCustomWorkingDirectory { get; }
    public Setting<string> WorkingDirectory { get; }
    public Setting<string> WindowState { get; }
    public Setting<bool> UseCustomSize { get; }
    public Setting<int> WindowWidth { get; }
    public Setting<int> WindowHeight { get; }
    public Setting<bool> MoveToMonitor { get; }
    public Setting<string> TargetMonitor { get; }
    public Setting<string> LifecycleMode { get; }
    public Setting<bool> BringToForeground { get; }

    public Action AutoDetectAction { get; }

    private readonly IReadOnlyList<ISetting> _allSettings;
    private readonly IReadOnlyList<IAction> _allActions;
    private readonly IAppDiscoveryService _appDiscovery;

    public Settings(IAppDiscoveryService appDiscovery)
    {
        _appDiscovery = appDiscovery;

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
            description: "How to handle the application process."
        );

        ApplicationPath = Setting.AsFilePicker(
            key: "ApplicationPath",
            label: "Application Path",
            description:
            "Path to the application executable. You can enter a simple name (e.g. 'chrome') and click 'Auto-Detect'.",
            defaultValue: "",
            filter: "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
        );

        ApplicationArgs = Setting.AsText(
            key: "ApplicationArgs",
            label: "Launch Arguments",
            description: "Command-line arguments to pass when launching a new process.",
            defaultValue: ""
        );

        UseCustomWorkingDirectory = Setting.AsCheckbox(
            key: "UseCustomWorkingDirectory",
            label: "Use Custom Working Directory",
            defaultValue: false,
            description:
            "If enabled, the application will be started with the specified working directory instead of the executable's folder."
        );

        WorkingDirectory = Setting.AsDirectoryPicker(
            key: "WorkingDirectory",
            label: "Working Directory",
            defaultValue: Environment.CurrentDirectory,
            description: "Custom working directory for the application.",
            isVisible: false
        );

        var monitorChoices = BuildMonitorChoices();

        MoveToMonitor = Setting.AsCheckbox(
            key: "MoveToMonitor",
            label: "Move Window To Monitor",
            defaultValue: false,
            description: "If enabled, the window will be moved to the selected monitor."
        );

        TargetMonitor = Setting.AsChoice(
            key: "TargetMonitor",
            label: "Target Monitor",
            defaultValue: monitorChoices[0].Key,
            initialChoices: monitorChoices,
            description: "Monitor to move the window to after it appears.",
            isVisible: false
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
            description: "Desired window state after session starts."
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
            description: "What happens to the process when session ends. 'Try Close' attempts graceful shutdown and may leave process open. 'Kill Immediately' terminates process without waiting."
        );

        BringToForeground = Setting.AsCheckbox(
            key: "BringToForeground",
            label: "Bring to Foreground",
            defaultValue: true,
            description: "Automatically bring the window to foreground after setup."
        );

        AutoDetectAction = Action.Create("AutoDetect", "Auto-Detect Path");
        AutoDetectAction.OnInvokeAsync(AutoDetectPathAsync);

        SetupReactiveVisibility();

        _allSettings =
        [
            ProcessMode,
            ApplicationPath,
            ApplicationArgs,
            UseCustomWorkingDirectory,
            WorkingDirectory,
            WindowState,
            UseCustomSize,
            WindowWidth,
            WindowHeight,
            MoveToMonitor,
            TargetMonitor,
            LifecycleMode,
            BringToForeground
        ];

        _allActions = [AutoDetectAction];
    }

    public Task InitializeAsync()
    {
        // Pre-load index in background if needed
        return Task.Run(() => _appDiscovery.GetInstalledApplicationsIndex());
    }

    private async Task AutoDetectPathAsync()
    {
        var currentInput = ApplicationPath.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(currentInput))
        {
            return;
        }

        // If it's already a full path, do nothing
        if (Path.IsPathRooted(currentInput) && File.Exists(currentInput))
        {
            return;
        }

        var foundPath = await Task.Run(() => _appDiscovery.FindKnownApp(currentInput));

        if (!string.IsNullOrEmpty(foundPath))
        {
            ApplicationPath.SetValue(foundPath);
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

        ProcessMode.Value.Subscribe(mode =>
        {
            var showArgs = mode is "LaunchNew" or "LaunchOrAttach";
            ApplicationArgs.SetVisibility(showArgs);
        });

        UseCustomWorkingDirectory.Value.Subscribe(useCustomWorkingDir =>
        {
            WorkingDirectory.SetVisibility(useCustomWorkingDir);
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
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(ApplicationPath.GetCurrentValue()))
        {
            errors[ApplicationPath.Key] = "Application Path is required.";
        }
        else
        {
            var mode = ProcessMode.GetCurrentValue();
            var path = ApplicationPath.GetCurrentValue();
            if (mode == "LaunchNew" && Path.IsPathRooted(path) && !File.Exists(path))
            {
                errors[ApplicationPath.Key] = $"File not found at '{path}'.";
            }
        }

        if (UseCustomWorkingDirectory.GetCurrentValue())
        {
            var workingDir = WorkingDirectory.GetCurrentValue();
            if (string.IsNullOrWhiteSpace(workingDir))
            {
                errors[WorkingDirectory.Key] = "Working Directory is required.";
            }
            else if (!Directory.Exists(workingDir))
            {
                errors[WorkingDirectory.Key] = $"Directory '{workingDir}' does not exist.";
            }
        }

        if (UseCustomSize.GetCurrentValue())
        {
            if (WindowWidth.GetCurrentValue() < 100)
            {
                errors[WindowWidth.Key] = "Width must be at least 100px.";
            }

            if (WindowHeight.GetCurrentValue() < 100)
            {
                errors[WindowHeight.Key] = "Height must be at least 100px.";
            }
        }

        if (errors.Count > 0)
        {
            return Task.FromResult(ValidationResult.Fail(errors, "Configuration contains errors."));
        }

        return Task.FromResult(ValidationResult.Success);
    }
}