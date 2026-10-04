using Axorith.Sdk.Logging;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.ApplicationLauncher.Apps.Discord;

internal sealed class DiscordApp(
    IModuleLogger logger,
    IAppDiscoveryService appDiscovery,
    IPlatformProcessService processService,
    IPlatformWindowService windowService)
    : LauncherAppBase(logger, processService, windowService)
{
    private const int MaxWindowWaitMs = 20_000;
    private const int WindowCheckIntervalMs = 500;
    private const int InitialPostWindowDelayMs = 200;
    private const int MaxPostWindowDelayMs = 3_000;
    private const int PostWindowRetries = 5;
    private static readonly string[] LoadingWindowTitles = ["Updating", "Starting", "Loading", "Checking"];
    private readonly Settings _settings = new(appDiscovery);

    protected override LauncherSettingsBase Settings => _settings;
    protected override bool ReattachOnWindowTimeout => false;
    protected override WindowConfigTimings GetWindowConfigTimings() => new(20_000, 500, 1_000, 500);
    protected override Task OnBeforeWindowConfigurationAsync(CancellationToken cancellationToken) =>
        WaitForDiscordMainWindowAsync(cancellationToken);

    private async Task WaitForDiscordMainWindowAsync(CancellationToken cancellationToken)
    {
        if (CurrentProcess is null or { HasExited: true })
        {
            return;
        }

        var deadline = DateTime.UtcNow.AddMilliseconds(MaxWindowWaitMs);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CurrentProcess.HasExited)
            {
                return;
            }

            CurrentProcess.Refresh();
            if (CurrentProcess.MainWindowHandle != IntPtr.Zero && !IsLoadingTitle(CurrentProcess.MainWindowTitle))
            {
                await WaitForWindowStabilizationAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await Task.Delay(WindowCheckIntervalMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WaitForWindowStabilizationAsync(CancellationToken cancellationToken)
    {
        var delay = InitialPostWindowDelayMs;
        for (var attempt = 0; attempt < PostWindowRetries; attempt++)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            if (CurrentProcess is null or { HasExited: true })
            {
                return;
            }

            CurrentProcess.Refresh();
            if (CurrentProcess.MainWindowHandle != IntPtr.Zero && !IsLoadingTitle(CurrentProcess.MainWindowTitle))
            {
                return;
            }

            delay = Math.Min(delay * 2, MaxPostWindowDelayMs);
        }
    }

    private static bool IsLoadingTitle(string title) =>
        string.IsNullOrWhiteSpace(title) || LoadingWindowTitles.Any(loading => title.Contains(loading, StringComparison.OrdinalIgnoreCase));
}
