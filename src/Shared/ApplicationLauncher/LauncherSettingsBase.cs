using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;

namespace Axorith.Shared.ApplicationLauncher;

/// <summary>
///     Abstract base class for launcher module settings.
///     Provides common settings for process mode, window state, lifecycle, and monitor selection.
///     Derived classes must implement ApplicationPath and can add additional settings.
/// </summary>
public abstract class LauncherSettingsBase
{
    /// <summary>
    ///     How to handle the application process (LaunchNew, AttachExisting, LaunchOrAttach).
    /// </summary>
    public Setting<string> ProcessMode { get; }

    /// <summary>
    ///     Desired window state after application starts (Normal, Maximized, Minimized).
    /// </summary>
    public Setting<string> WindowState { get; }

    /// <summary>
    ///     Enable custom window dimensions. Requires Normal window state.
    /// </summary>
    public Setting<bool> UseCustomSize { get; }

    /// <summary>
    ///     Custom window width in pixels.
    /// </summary>
    public Setting<int> WindowWidth { get; }

    /// <summary>
    ///     Custom window height in pixels.
    /// </summary>
    public Setting<int> WindowHeight { get; }

    /// <summary>
    ///     If enabled, window will be moved to the selected monitor.
    /// </summary>
    public Setting<bool> MoveToMonitor { get; }

    /// <summary>
    ///     Target monitor index for window placement.
    /// </summary>
    public Setting<string> TargetMonitor { get; }

    /// <summary>
    ///     What happens to the process when session ends.
    /// </summary>
    public Setting<string> LifecycleMode { get; }

    /// <summary>
    ///     Automatically bring the window to foreground after setup.
    /// </summary>
    public Setting<bool> BringToForeground { get; }

    /// <summary>
    ///     Path to the application executable. Must be implemented by derived classes.
    /// </summary>
    public abstract Setting<string> ApplicationPath { get; }

    private readonly List<ISetting> _baseSettings;
    private readonly List<IAction> _baseActions = [];

    protected LauncherSettingsBase()
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
            description: "Desired window state after application starts."
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
            description: "Monitor to move the window to after it appears.",
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
            description: "What happens to the process when session ends."
        );

        BringToForeground = Setting.AsCheckbox(
            key: "BringToForeground",
            label: "Bring to Foreground",
            defaultValue: true,
            description: "Automatically bring the window to foreground after setup."
        );

        _baseSettings =
        [
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

        // Note: SetupBaseReactiveVisibility() is NOT called here
        // It must be called explicitly by derived classes after their fields are initialized
    }

    /// <summary>
    ///     Gets all settings including base and additional settings from derived class.
    /// </summary>
    public IReadOnlyList<ISetting> GetAllSettings()
    {
        var result = new List<ISetting> { ApplicationPath };
        result.AddRange(GetAdditionalSettingsBeforeBase());
        result.AddRange(_baseSettings);
        result.AddRange(GetAdditionalSettings());
        return result;
    }

    /// <summary>
    ///     Gets all actions including base and additional actions from derived class.
    /// </summary>
    public IReadOnlyList<IAction> GetAllActions()
    {
        var result = new List<IAction>(_baseActions);
        result.AddRange(GetAdditionalActions());
        return result;
    }


    /// <summary>
    ///     Initializes settings. Called when module is created for editing.
    /// </summary>
    public async Task InitializeAsync()
    {
        await InitializeAdditionalAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Validates all settings including base and additional validation.
    /// </summary>
    public async Task<ValidationResult> ValidateAsync()
    {
        var errors = new Dictionary<string, string>();

        var appPath = ApplicationPath.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(appPath))
        {
            errors[ApplicationPath.Key] = $"'{((ISetting)ApplicationPath).GetCurrentLabel()}' is required.";
        }
        else
        {
            var mode = ProcessMode.GetCurrentValue();
            if (mode == "LaunchNew" && Path.IsPathRooted(appPath) && !File.Exists(appPath))
            {
                errors[ApplicationPath.Key] = $"File not found at '{appPath}'.";
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

        var additionalResult = await ValidateAdditionalAsync().ConfigureAwait(false);
        if (additionalResult.Status != ValidationStatus.Ok)
        {
            foreach (var fieldError in additionalResult.FieldErrors)
            {
                errors[fieldError.Key] = fieldError.Value;
            }

            if (errors.Count == 0 && !string.IsNullOrEmpty(additionalResult.Message))
            {
                return additionalResult;
            }
        }

        return errors.Count > 0
            ? ValidationResult.Fail(errors, "Configuration contains errors.")
            : ValidationResult.Success;
    }

    /// <summary>
    ///     Override to provide additional settings that appear before base settings.
    ///     Default returns empty list.
    /// </summary>
    protected virtual IEnumerable<ISetting> GetAdditionalSettingsBeforeBase()
    {
        return [];
    }

    /// <summary>
    ///     Override to provide additional settings that appear after base settings.
    /// </summary>
    protected abstract IEnumerable<ISetting> GetAdditionalSettings();

    /// <summary>
    ///     Override to provide additional actions.
    /// </summary>
    protected abstract IEnumerable<IAction> GetAdditionalActions();

    /// <summary>
    ///     Override to perform additional initialization.
    /// </summary>
    protected virtual Task InitializeAdditionalAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Override to perform additional validation.
    /// </summary>
    protected virtual Task<ValidationResult> ValidateAdditionalAsync()
    {
        return Task.FromResult(ValidationResult.Success);
    }

    /// <summary>
    ///     Override to setup additional reactive visibility rules.
    ///     Called after base visibility rules are set up.
    /// </summary>
    protected virtual void SetupAdditionalReactiveVisibility()
    {
    }

    /// <summary>
    ///     Sets up reactive visibility for base settings.
    ///     MUST be called by derived classes after all their fields are initialized.
    /// </summary>
    protected void SetupBaseReactiveVisibility()
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

        MoveToMonitor.Value.Subscribe(move => TargetMonitor.SetVisibility(move));

        SetupAdditionalReactiveVisibility();
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
}