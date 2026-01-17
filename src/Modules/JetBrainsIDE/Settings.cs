using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.JetBrainsIDE;

internal sealed class Settings : LauncherSettingsBase
{
    public override Setting<string> ApplicationPath => IdePath;

    public Setting<string> IdePath { get; }
    public Setting<string> ProjectPath { get; }
    public Setting<string> ApplicationArgs { get; }

    public Action RefreshIdeListAction { get; }

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
            defaultValue: "",
            description: "Path to the solution or project directory to open in IDE."
        );

        ApplicationArgs = Setting.AsText(
            key: "ApplicationArgs",
            label: "Launch Arguments",
            description: "Additional command-line arguments to pass to the IDE.",
            defaultValue: ""
        );

        RefreshIdeListAction = Action.Create("RefreshIdeList", "Refresh IDE List");
        RefreshIdeListAction.OnInvokeAsync(RefreshIdeListAsync);

        SetupBaseReactiveVisibility();
    }

    protected override IEnumerable<ISetting> GetAdditionalSettingsBeforeBase()
    {
        yield return ProjectPath;
    }

    protected override IEnumerable<ISetting> GetAdditionalSettings()
    {
        yield return ApplicationArgs;
    }

    protected override IEnumerable<IAction> GetAdditionalActions()
    {
        yield return RefreshIdeListAction;
    }

    protected override Task InitializeAdditionalAsync()
    {
        return RefreshIdeListAsync();
    }

    protected override void SetupAdditionalReactiveVisibility()
    {
        ProcessMode.Value.Subscribe(mode =>
        {
            var showArgs = mode is "LaunchNew" or "LaunchOrAttach";
            ApplicationArgs.SetVisibility(showArgs);
        });
    }

    protected override Task<ValidationResult> ValidateAdditionalAsync()
    {
        var projectPath = ProjectPath.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return Task.FromResult(ValidationResult.Fail(
                new Dictionary<string, string> { [ProjectPath.Key] = "Project path is required." },
                "Project path is required."));
        }

        if (!Directory.Exists(projectPath) && !File.Exists(projectPath))
        {
            return Task.FromResult(ValidationResult.Fail(
                new Dictionary<string, string> { [ProjectPath.Key] = $"Path not found: '{projectPath}'." },
                "Project path not found."));
        }

        return Task.FromResult(ValidationResult.Success);
    }

    private async Task RefreshIdeListAsync()
    {
        var apps = await Task.Run(() => _appDiscovery.FindAppsByPublisher("JetBrains")).ConfigureAwait(false);

        var choices =
            (from app in apps
                where !app.Name.Contains("Toolbox", StringComparison.OrdinalIgnoreCase)
                select new KeyValuePair<string, string>(app.ExecutablePath, app.Name)).ToList();

        if (choices.Count == 0)
        {
            choices.Add(new KeyValuePair<string, string>("", "No JetBrains IDEs found"));
        }

        var current = IdePath.GetCurrentValue();
        if (!string.IsNullOrEmpty(current) && choices.All(c => c.Key != current))
        {
            choices.Insert(0, new KeyValuePair<string, string>(current, $"{current} (Custom)"));
        }

        IdePath.SetChoices(choices);

        if (string.IsNullOrEmpty(current) && choices.Count > 0 && !string.IsNullOrEmpty(choices[0].Key))
        {
            IdePath.SetValue(choices[0].Key);
        }
    }

    public override void Dispose()
    {
        IdePath.Dispose();
        ProjectPath.Dispose();
        ApplicationArgs.Dispose();
        RefreshIdeListAction.Dispose();
        base.Dispose();
    }
}