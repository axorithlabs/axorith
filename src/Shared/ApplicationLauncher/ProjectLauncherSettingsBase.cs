using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;

namespace Axorith.Shared.ApplicationLauncher;

public abstract class ProjectLauncherSettingsBase : LauncherSettingsBase
{
    protected ProjectLauncherSettingsBase(
        Setting<string> applicationPath,
        Setting<string> projectPath,
        string argumentsDescription)
    {
        ApplicationPath = applicationPath;
        ProjectPath = projectPath;
        ApplicationArgs = Setting.AsText("ApplicationArgs", "Launch Arguments", "", argumentsDescription);
        SetupBaseReactiveVisibility();
    }

    public override Setting<string> ApplicationPath { get; }
    public Setting<string> ProjectPath { get; }
    public Setting<string> ApplicationArgs { get; }

    protected override IEnumerable<ISetting> GetAdditionalSettingsBeforeBase() => [ProjectPath];
    protected override IEnumerable<ISetting> GetAdditionalSettings() => [ApplicationArgs];

    protected override void SetupAdditionalReactiveVisibility() =>
        ProcessMode.Value.Subscribe(mode => ApplicationArgs.SetVisibility(mode is "LaunchNew" or "LaunchOrAttach"));

    protected override Task<ValidationResult> ValidateAdditionalAsync()
    {
        var path = ProjectPath.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(ValidationResult.Fail(
                new Dictionary<string, string> { [ProjectPath.Key] = "Project path is required." },
                "Project path is required."));
        }

        if (!ProjectPathExists(path))
        {
            return Task.FromResult(ValidationResult.Fail(
                new Dictionary<string, string> { [ProjectPath.Key] = $"Path not found: '{path}'." },
                "Project path not found."));
        }

        return Task.FromResult(ValidationResult.Success);
    }

    protected abstract bool ProjectPathExists(string path);

}
