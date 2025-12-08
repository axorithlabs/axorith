using Axorith.Sdk.Logging;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.ApplicationLauncher;

public class Module(IModuleLogger logger, IAppDiscoveryService appDiscovery) : LauncherModuleBase(logger)
{
    private readonly Settings _settings = new(appDiscovery);

    protected override LauncherSettingsBase Settings => _settings;

    protected override string GetLaunchArguments()
    {
        return _settings.ApplicationArgs.GetCurrentValue();
    }

    protected override string? GetWorkingDirectory()
    {
        return _settings.UseCustomWorkingDirectory.GetCurrentValue()
            ? _settings.WorkingDirectory.GetCurrentValue()
            : null;
    }
}