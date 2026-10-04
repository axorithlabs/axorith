using Axorith.Sdk;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;
using Axorith.Shared.Utils;

namespace Axorith.Module.ApplicationLauncher.Apps.VSCode;

internal sealed class Settings(IAppDiscoveryService appDiscovery) : ProjectLauncherSettingsBase(
    Setting.AsChoice(
        "CodePath",
        "VS Code Executable",
        string.Empty,
        [new KeyValuePair<string, string>("", "Scanning for VS Code...")],
        "Path to Visual Studio Code executable."),
    Setting.AsDirectoryPicker("ProjectPath", "Project Path", "", "Path to the folder or workspace to open."),
    "Additional command-line arguments (e.g. --disable-extensions).")
{
    protected override Task InitializeAdditionalAsync() => RefreshPathAsync();
    protected override bool ProjectPathExists(string path) => Directory.Exists(path);

    private async Task RefreshPathAsync()
    {
        var exeName = EnvironmentUtils.GetCurrentPlatform() == Platform.Windows ? "Code.exe" : "code";
        var path = await Task.Run(() => appDiscovery.FindKnownApp(exeName, "Visual Studio Code", "Code"))
            .ConfigureAwait(false);
        SetDetectedApplicationPath(path, "Visual Studio Code (Auto-Detected)", "VS Code not found");
    }
}
