using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.ApplicationLauncher;

internal sealed class Settings : LauncherSettingsBase
{
    public override Setting<string> ApplicationPath { get; }
    public Setting<string> ApplicationArgs { get; }
    public Setting<bool> UseCustomWorkingDirectory { get; }
    public Setting<string> WorkingDirectory { get; }

    public Action AutoDetectAction { get; }

    private readonly IAppDiscoveryService _appDiscovery;

    public Settings(IAppDiscoveryService appDiscovery)
    {
        _appDiscovery = appDiscovery;

        ApplicationPath = Setting.AsFilePicker(
            key: "ApplicationPath",
            label: "Application Path",
            description: "Path to the application executable. You can enter a simple name (e.g. 'chrome') and click 'Auto-Detect'.",
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
            description: "If enabled, the application will be started with the specified working directory instead of the executable's folder."
        );

        WorkingDirectory = Setting.AsDirectoryPicker(
            key: "WorkingDirectory",
            label: "Working Directory",
            defaultValue: Environment.CurrentDirectory,
            description: "Custom working directory for the application.",
            isVisible: false
        );

        AutoDetectAction = Action.Create("AutoDetect", "Auto-Detect Path");
        AutoDetectAction.OnInvokeAsync(AutoDetectPathAsync);

        // Setup reactive visibility after all fields are initialized
        SetupBaseReactiveVisibility();
    }

    protected override IEnumerable<ISetting> GetAdditionalSettings()
    {
        yield return ApplicationArgs;
        yield return UseCustomWorkingDirectory;
        yield return WorkingDirectory;
    }

    protected override IEnumerable<IAction> GetAdditionalActions()
    {
        yield return AutoDetectAction;
    }

    protected override Task InitializeAdditionalAsync()
    {
        // Pre-load index in background if needed
        return Task.Run(() => _appDiscovery.GetInstalledApplicationsIndex());
    }

    protected override Task<ValidationResult> ValidateAdditionalAsync()
    {
        if (!UseCustomWorkingDirectory.GetCurrentValue())
        {
            return Task.FromResult(ValidationResult.Success);
        }

        var workingDir = WorkingDirectory.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(workingDir))
        {
            return Task.FromResult(ValidationResult.Fail(
                new Dictionary<string, string> { [WorkingDirectory.Key] = "Working Directory is required." },
                "Configuration contains errors."));
        }

        if (!Directory.Exists(workingDir))
        {
            return Task.FromResult(ValidationResult.Fail(
                new Dictionary<string, string> { [WorkingDirectory.Key] = $"Directory '{workingDir}' does not exist." },
                "Configuration contains errors."));
        }

        return Task.FromResult(ValidationResult.Success);
    }

    protected override void SetupAdditionalReactiveVisibility()
    {
        ProcessMode.Value.Subscribe(mode =>
        {
            var showArgs = mode is "LaunchNew" or "LaunchOrAttach";
            ApplicationArgs.SetVisibility(showArgs);
        });

        UseCustomWorkingDirectory.Value.Subscribe(useCustomWorkingDir =>
        {
            WorkingDirectory.SetVisibility(useCustomWorkingDir);
        });
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

        var foundPath = await Task.Run(() => _appDiscovery.FindKnownApp(currentInput)).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(foundPath))
        {
            ApplicationPath.SetValue(foundPath);
        }
    }
}
