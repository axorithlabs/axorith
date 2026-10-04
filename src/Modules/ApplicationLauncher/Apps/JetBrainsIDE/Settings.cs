using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.ApplicationLauncher.Apps.JetBrainsIDE;

internal sealed class Settings(IAppDiscoveryService appDiscovery) : ProjectLauncherSettingsBase(
    Setting.AsChoice(
        "IDEPath",
        "IDE Executable",
        string.Empty,
        [new KeyValuePair<string, string>("", "Scanning for IDEs...")],
        "Select installed JetBrains IDE or enter custom path."),
    Setting.AsFilePicker("ProjectPath", "Project Path", "", "Path to the solution or project directory to open in IDE."),
    "Additional command-line arguments to pass to the IDE.")
{
    protected override Task InitializeAdditionalAsync() => RefreshIdeListAsync();
    protected override bool ProjectPathExists(string path) => Directory.Exists(path) || File.Exists(path);

    private async Task RefreshIdeListAsync()
    {
        var apps = await Task.Run(() => appDiscovery.FindAppsByPublisher("JetBrains")).ConfigureAwait(false);
        var choices = apps
            .Where(app => !app.Name.Contains("Toolbox", StringComparison.OrdinalIgnoreCase))
            .Select(app => new KeyValuePair<string, string>(app.ExecutablePath, app.Name))
            .ToList();

        if (choices.Count == 0)
        {
            choices.Add(new("", "No JetBrains IDEs found"));
        }

        var current = ApplicationPath.GetCurrentValue();
        if (!string.IsNullOrEmpty(current) && choices.All(choice => choice.Key != current))
        {
            choices.Insert(0, new(current, $"{current} (Custom)"));
        }

        ApplicationPath.SetChoices(choices);
        if (string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(choices[0].Key))
        {
            ApplicationPath.SetValue(choices[0].Key);
        }
    }
}
