using Axorith.Sdk.Logging;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.JBIDELauncher;

public class Module(IModuleLogger logger, IAppDiscoveryService appDiscovery) : LauncherModuleBase(logger)
{
    private readonly Settings _settings = new(appDiscovery);

    protected override LauncherSettingsBase Settings => _settings;

    protected override string GetLaunchArguments()
    {
        var args = _settings.ApplicationArgs.GetCurrentValue();
        var projectPath = _settings.ProjectPath.GetCurrentValue();

        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            args = string.IsNullOrWhiteSpace(args) ? projectPath : $"{args} {projectPath}";
        }

        return args;
    }

    protected override string? GetWorkingDirectory()
    {
        var projectPath = _settings.ProjectPath.GetCurrentValue();

        try
        {
            if (!string.IsNullOrWhiteSpace(projectPath) && Directory.Exists(projectPath))
            {
                return projectPath;
            }
        }
        catch
        {
            // Ignore invalid project path and fall back to IDE directory
        }

        return null;
    }

    protected override WindowConfigTimings GetWindowConfigTimings()
    {
        // JetBrains IDEs are slow to start - use longer timeouts
        return new WindowConfigTimings(
            WaitForWindowTimeoutMs: 15000,
            MoveDelayMs: 5000,
            MaximizeSnapDelayMs: 5000,
            FinalFocusDelayMs: 2000,
            BannerDelayMs: 0
        );
    }
}