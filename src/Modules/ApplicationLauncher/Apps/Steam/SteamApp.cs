using System.Diagnostics;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.ApplicationLauncher.Apps.Steam;

internal sealed class SteamApp(
    IModuleLogger logger,
    INotifier notifier,
    IAppDiscoveryService appDiscovery,
    IPlatformProcessService processService,
    IPlatformWindowService windowService)
    : LauncherAppBase(logger, processService, windowService)
{
    private readonly Settings _settings = new(appDiscovery);

    protected override LauncherSettingsBase Settings => _settings;
    protected override bool ReattachOnWindowTimeout => false;
    protected override WindowConfigTimings GetWindowConfigTimings() => new(30_000, 500, 1_000, 500);

    public override async Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApplicationPath.GetCurrentValue()))
        {
            Logger.LogError(null, "Steam path not configured");
            notifier.ShowToast("Steam: Path not configured.", NotificationType.Error, "Steam");
            return;
        }

        await base.OnSessionStartAsync(cancellationToken).ConfigureAwait(false);
        if (CurrentProcess == null)
        {
            notifier.ShowToast("Steam: Failed to launch.", NotificationType.Error, "Steam");
            return;
        }

        var gameAppId = _settings.SelectedGame.GetCurrentValue();
        if (!string.IsNullOrWhiteSpace(gameAppId))
        {
            LaunchGame(gameAppId);
        }
    }

    private void LaunchGame(string appId)
    {
        try
        {
            Logger.LogInfo("Launching game with AppID {AppId}", appId);
            Process.Start(new ProcessStartInfo($"steam://rungameid/{appId}") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to launch game {AppId}", appId);
            notifier.ShowToast("Steam: Failed to launch game.", NotificationType.Error, "Steam");
        }
    }
}
