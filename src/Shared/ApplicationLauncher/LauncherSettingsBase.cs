using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;

namespace Axorith.Shared.ApplicationLauncher;

public abstract class LauncherSettingsBase : IDisposable
{
    public Setting<string> ProcessMode { get; }

    public Setting<string> WindowState { get; }

    public Setting<bool> UseCustomSize { get; }

    public Setting<int> WindowWidth { get; }

    public Setting<int> WindowHeight { get; }

    public Setting<bool> MoveToMonitor { get; }

    public Setting<string> TargetMonitor { get; }

    public Setting<string> LifecycleMode { get; }

    public Setting<bool> BringToForeground { get; }

    public abstract Setting<string> ApplicationPath { get; }

    private readonly List<ISetting> _baseSettings;

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

    public IReadOnlyList<ISetting> GetAllSettings() =>
        [ApplicationPath, .. GetAdditionalSettingsBeforeBase(), .. _baseSettings, .. GetAdditionalSettings()];

    public IReadOnlyList<IAction> GetAllActions() => GetAdditionalActions().ToArray();

    public Task InitializeAsync() => InitializeAdditionalAsync();

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
            if (mode != "AttachExisting" && Path.IsPathRooted(appPath) && !File.Exists(appPath))
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

    protected virtual IEnumerable<ISetting> GetAdditionalSettingsBeforeBase() => [];
    protected virtual IEnumerable<ISetting> GetAdditionalSettings() => [];
    protected virtual IEnumerable<IAction> GetAdditionalActions() => [];
    protected virtual Task InitializeAdditionalAsync() => Task.CompletedTask;
    protected virtual Task<ValidationResult> ValidateAdditionalAsync() => Task.FromResult(ValidationResult.Success);
    protected virtual void SetupAdditionalReactiveVisibility() { }

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

    protected void SetDetectedApplicationPath(string? path, string detectedLabel, string notFoundLabel,
        Func<string, string>? customLabel = null)
    {
        var current = ApplicationPath.GetCurrentValue();
        var choices = new List<KeyValuePair<string, string>>
        {
            !string.IsNullOrEmpty(path) ? new(path, detectedLabel) : new("", notFoundLabel)
        };

        if (!string.IsNullOrEmpty(current) && choices.All(choice => !choice.Key.Equals(current, StringComparison.OrdinalIgnoreCase)))
            choices.Insert(0, new(current, customLabel?.Invoke(current) ?? $"{current} (Custom)"));

        ApplicationPath.SetChoices(choices);
        if (string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(path))
            ApplicationPath.SetValue(path);
    }

    private static IReadOnlyList<KeyValuePair<string, string>> BuildMonitorChoices()
    {
        var windowService = PlatformServices.CreateWindowService();
        return Enumerable.Range(0, Math.Max(1, windowService.GetMonitorCount()))
            .Select(i => new KeyValuePair<string, string>(i.ToString(), $"{i}: {windowService.GetMonitorName(i)}"))
            .ToArray();
    }

    public virtual void Dispose()
    {
        foreach (var setting in GetAllSettings().OfType<IDisposable>().Distinct())
            setting.Dispose();
        foreach (var action in GetAllActions().OfType<IDisposable>().Distinct())
            action.Dispose();
        GC.SuppressFinalize(this);
    }
}
