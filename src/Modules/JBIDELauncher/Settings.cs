using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.JBIDELauncher;

internal sealed class Settings
{
    public Setting<string> IdePath { get; }
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

    public Action RefreshIdeListAction { get; }

    private readonly IReadOnlyList<ISetting> _allSettings;
    private readonly IReadOnlyList<IAction> _allActions;
    private readonly IAppDiscoveryService _appDiscovery;

    public Settings(IAppDiscoveryService appDiscovery)
    {
        _appDiscovery = appDiscovery;

        IdePath = Setting.AsChoice(
            key: "IDEPath",
            label: "IDE Executable",
            defaultValue: string.Empty,
            initialChoices: [new KeyValuePair<string, string>("", "Scanning for IDEs...")],
            description: "Select installed JetBrains IDE or enter custom path."
        );

        ProjectPath = Setting.AsFilePicker(
            key: "ProjectPath",
            label: "Project Path",
            defaultValue: Environment.CurrentDirectory,
            description: "Path to the solution or project directory to open in IDE."
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
            description: "How to handle the IDE process."
        );

        ApplicationArgs = Setting.AsText(
            key: "ApplicationArgs",
            label: "Launch Arguments",
            description: "Additional command-line arguments to pass to the IDE.",
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
            description: "Desired window state after IDE starts."
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
            description: "If enabled, IDE window will be moved to the selected monitor."
        );

        TargetMonitor = Setting.AsChoice(
            key: "TargetMonitor",
            label: "Target Monitor",
            defaultValue: monitorChoices[0].Key,
            initialChoices: monitorChoices,
            description: "Monitor to move the IDE window to after it appears.",
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
            description: "What happens to IDE when session ends."
        );

        BringToForeground = Setting.AsCheckbox(
            key: "BringToForeground",
            label: "Bring to Foreground",
            defaultValue: true,
            description: "Automatically bring the IDE window to foreground after setup."
        );

        RefreshIdeListAction = Action.Create("RefreshIdeList", "Refresh IDE List");
        RefreshIdeListAction.OnInvokeAsync(RefreshIdeListAsync);

        SetupReactiveVisibility();

        _allSettings =
        [
            IdePath,
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

        _allActions = [RefreshIdeListAction];
    }

    public Task InitializeAsync()
    {
        return RefreshIdeListAsync();
    }

    private async Task RefreshIdeListAsync()
    {
        var apps = await Task.Run(() => _appDiscovery.FindAppsByPublisher("JetBrains"));

        var choices = new List<KeyValuePair<string, string>>();

        foreach (var app in apps)
        {
            // Filter out Toolbox itself and non-IDE tools if possible, but generally listing all is safer
            if (app.Name.Contains("Toolbox", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            choices.Add(new KeyValuePair<string, string>(app.ExecutablePath, app.Name));
        }

        if (choices.Count == 0)
        {
            choices.Add(new KeyValuePair<string, string>("", "No JetBrains IDEs found"));
        }

        // Preserve current value if it's custom
        var current = IdePath.GetCurrentValue();
        if (!string.IsNullOrEmpty(current) && !choices.Any(c => c.Key == current))
        {
            choices.Insert(0, new KeyValuePair<string, string>(current, $"{current} (Custom)"));
        }

        IdePath.SetChoices(choices);

        // Auto-select first if empty
        if (string.IsNullOrEmpty(current) && choices.Count > 0 && !string.IsNullOrEmpty(choices[0].Key))
        {
            IdePath.SetValue(choices[0].Key);
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
        var idePath = IdePath.GetCurrentValue();

        if (string.IsNullOrWhiteSpace(idePath))
        {
            return Task.FromResult(ValidationResult.Fail("'IDE Executable' is required."));
        }

        var mode = ProcessMode.GetCurrentValue();
        if (mode == "LaunchNew" && !File.Exists(idePath))
        {
            return Task.FromResult(ValidationResult.Fail($"IDE executable not found at '{idePath}'."));
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