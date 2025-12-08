using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;
using Axorith.Shared.Utils;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.VSCodeLauncher;

internal sealed class Settings : LauncherSettingsBase
{
    public override Setting<string> ApplicationPath => CodePath;

    public Setting<string> CodePath { get; }
    public Setting<string> ProjectPath { get; }
    public Setting<string> ApplicationArgs { get; }

    public Action RefreshPathAction { get; }

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

        ApplicationArgs = Setting.AsText(
            key: "ApplicationArgs",
            label: "Launch Arguments",
            description: "Additional command-line arguments (e.g. --disable-extensions).",
            defaultValue: ""
        );

        RefreshPathAction = Action.Create("RefreshPath", "Refresh Path");
        RefreshPathAction.OnInvokeAsync(RefreshPathAsync);

        // Setup reactive visibility after all fields are initialized
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
        yield return RefreshPathAction;
    }

    protected override Task InitializeAdditionalAsync()
    {
        return RefreshPathAsync();
    }

    protected override void SetupAdditionalReactiveVisibility()
    {
        ProcessMode.Value.Subscribe(mode =>
        {
            var showArgs = mode is "LaunchNew" or "LaunchOrAttach";
            ApplicationArgs.SetVisibility(showArgs);
        });
    }

    private async Task RefreshPathAsync()
    {
        var platform = EnvironmentUtils.GetCurrentPlatform();
        var exeName = platform == Platform.Windows ? "Code.exe" : "code";

        var path = await Task.Run(() => _appDiscovery.FindKnownApp(exeName, "Visual Studio Code", "Code")).ConfigureAwait(false);

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
}
