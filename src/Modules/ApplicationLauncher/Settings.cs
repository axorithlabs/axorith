using Axorith.Sdk;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;

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

    private readonly IReadOnlyList<ISetting> _allSettings;

    public Settings()
    {
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
            "Path to the application executable. Used for launching new processes or finding existing ones.",
            defaultValue: @"C:\\Windows\\notepad.exe",
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
            defaultValue: "TerminateOnEnd",
            initialChoices:
            [
                new KeyValuePair<string, string>("TerminateOnEnd", "Terminate on Session End"),
                new KeyValuePair<string, string>("KeepRunning", "Keep Running")
            ],
            description: "What happens to the process when session ends."
        );

        BringToForeground = Setting.AsCheckbox(
            key: "BringToForeground",
            label: "Bring to Foreground",
            defaultValue: true,
            description: "Automatically bring the window to foreground after setup."
        );

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
            monitorCount = 1;

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

    public Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ApplicationPath.GetCurrentValue()))
            return Task.FromResult(ValidationResult.Fail("'Application Path' is required."));

        var mode = ProcessMode.GetCurrentValue();
        if (mode == "LaunchNew" && !File.Exists(ApplicationPath.GetCurrentValue()))
            return Task.FromResult(ValidationResult.Fail($"File not found at '{ApplicationPath.GetCurrentValue()}'."));

        if (UseCustomWorkingDirectory.GetCurrentValue())
        {
            var workingDir = WorkingDirectory.GetCurrentValue();
            if (string.IsNullOrWhiteSpace(workingDir))
                return Task.FromResult(
                    ValidationResult.Fail("'Working Directory' is required when custom working directory is enabled."));

            if (!Directory.Exists(workingDir))
                return Task.FromResult(ValidationResult.Fail($"Working directory '{workingDir}' does not exist."));
        }

        if (!UseCustomSize.GetCurrentValue()) return Task.FromResult(ValidationResult.Success);

        if (WindowWidth.GetCurrentValue() < 100)
            return Task.FromResult(ValidationResult.Fail("'Window Width' must be at least 100 pixels."));
        if (WindowHeight.GetCurrentValue() < 100)
            return Task.FromResult(ValidationResult.Fail("'Window Height' must be at least 100 pixels."));

        return Task.FromResult(ValidationResult.Success);
    }
}