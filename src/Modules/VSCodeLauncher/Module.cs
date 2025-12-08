using Axorith.Sdk.Logging;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.VSCodeLauncher;

public class Module(IModuleLogger logger, IAppDiscoveryService appDiscovery) : LauncherModuleBase(logger)
{
    private readonly Settings _settings = new(appDiscovery);

    protected override LauncherSettingsBase Settings => _settings;

    protected override string GetLaunchArguments()
    {
        var args = _settings.ApplicationArgs.GetCurrentValue();
        var projectPath = _settings.ProjectPath.GetCurrentValue();

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return args;
        }

        // Quote path if it contains spaces
        var safePath = projectPath.Contains(' ') ? $"\"{projectPath}\"" : projectPath;
        args = string.IsNullOrWhiteSpace(args) ? safePath : $"{args} {safePath}";

        return args;
    }

    protected override WindowConfigTimings GetWindowConfigTimings()
    {
        // Electron apps can be slow to start
        return new WindowConfigTimings(
            WaitForWindowTimeoutMs: 10000,
            MoveDelayMs: 1000,
            MaximizeSnapDelayMs: 500,
            FinalFocusDelayMs: 500,
            BannerDelayMs: 0
        );
    }
}