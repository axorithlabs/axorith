using Axorith.Sdk.Logging;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.SpotifyLauncher;

public class Module(IModuleLogger logger, IAppDiscoveryService appDiscovery) : LauncherModuleBase(logger)
{
    private readonly Settings _settings = new(appDiscovery);

    protected override LauncherSettingsBase Settings => _settings;

    protected override WindowConfigTimings GetWindowConfigTimings()
    {
        // Spotify-specific behavior: delay move/maximize operations to avoid fighting with splash/banner
        return new WindowConfigTimings(
            WaitForWindowTimeoutMs: 5000,
            MoveDelayMs: 1000,
            MaximizeSnapDelayMs: 1000,
            FinalFocusDelayMs: 1000,
            BannerDelayMs: 0
        );
    }
}