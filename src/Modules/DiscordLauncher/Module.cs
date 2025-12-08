using Axorith.Sdk.Logging;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.DiscordLauncher;

public class Module(IModuleLogger logger, IAppDiscoveryService appDiscovery) : LauncherModuleBase(logger)
{
    private readonly Settings _settings = new(appDiscovery);

    protected override LauncherSettingsBase Settings => _settings;

    protected override WindowConfigTimings GetWindowConfigTimings()
    {
        // Discord has a splash screen, use longer timeouts
        return new WindowConfigTimings(
            WaitForWindowTimeoutMs: 20000,
            MoveDelayMs: 500,
            MaximizeSnapDelayMs: 1000,
            FinalFocusDelayMs: 500,
            BannerDelayMs: 0
        );
    }

    protected override async Task OnBeforeWindowConfigurationAsync(CancellationToken cancellationToken)
    {
        // Wait for Discord main window (skip splash screen)
        await WaitForDiscordMainWindowAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task OnAfterWindowConfigurationAsync(CancellationToken cancellationToken)
    {
        // Discord-specific window configuration
        await ConfigureDiscordWindowAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WaitForDiscordMainWindowAsync(CancellationToken cancellationToken)
    {
        if (CurrentProcess == null || CurrentProcess.HasExited)
        {
            return;
        }

        Logger.LogInfo("Waiting for Discord main window (skipping splash screen)...");

        var startTime = DateTime.Now;
        const int maxWaitMs = 20000;
        const int checkIntervalMs = 500;

        while ((DateTime.Now - startTime).TotalMilliseconds < maxWaitMs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (CurrentProcess.HasExited)
            {
                Logger.LogWarning("Discord process exited while waiting for main window");
                return;
            }

            CurrentProcess.Refresh();

            if (CurrentProcess.MainWindowHandle != IntPtr.Zero)
            {
                var title = CurrentProcess.MainWindowTitle;

                if (!string.IsNullOrWhiteSpace(title) &&
                    !title.Equals("Discord", StringComparison.OrdinalIgnoreCase) &&
                    !title.Contains("Updating", StringComparison.OrdinalIgnoreCase) &&
                    !title.Contains("Starting", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogInfo("Discord main window detected with title: {Title}", title);

                    await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            await Task.Delay(checkIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        Logger.LogWarning("Discord main window did not appear with expected title within timeout");
    }

    private async Task ConfigureDiscordWindowAsync(CancellationToken cancellationToken)
    {
        if (CurrentProcess == null || CurrentProcess.HasExited)
        {
            Logger.LogWarning("Discord process is null or exited, cannot configure window");
            return;
        }

        CurrentProcess.Refresh();

        if (CurrentProcess.MainWindowHandle == IntPtr.Zero)
        {
            Logger.LogWarning("Discord has no main window handle");
            return;
        }

        var windowHandle = CurrentProcess.MainWindowHandle;
        Logger.LogInfo("Configuring Discord window (Handle: {Handle}, Title: {Title})",
            windowHandle, CurrentProcess.MainWindowTitle);

        var state = _settings.WindowState.GetCurrentValue();
        var moveToMonitor = _settings.MoveToMonitor.GetCurrentValue();
        var monitorIndex = _settings.TargetMonitor.GetCurrentValue();

        if (moveToMonitor && !string.IsNullOrWhiteSpace(monitorIndex) && int.TryParse(monitorIndex, out var idx))
        {
            Logger.LogInfo("Moving Discord window to monitor {MonitorIndex}", idx);
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            PublicApi.MoveWindowToMonitor(windowHandle, idx);
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        switch (state)
        {
            case "Maximized":
                Logger.LogInfo("Maximizing Discord window");
                PublicApi.SetWindowState(windowHandle, WindowState.Maximized);
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);

                if (moveToMonitor && !string.IsNullOrWhiteSpace(monitorIndex) &&
                    int.TryParse(monitorIndex, out var snapIdx))
                {
                    var (mx, my, mWidth, mHeight) = PublicApi.GetMonitorBounds(snapIdx);
                    var (wx, wy, wWidth, wHeight) = PublicApi.GetWindowBounds(windowHandle);

                    if (wx != mx || wy != my || wWidth != mWidth || wHeight != mHeight)
                    {
                        Logger.LogInfo("Snapping Discord window to monitor bounds");
                        PublicApi.SetWindowPosition(windowHandle, mx, my);
                        PublicApi.SetWindowSize(windowHandle, mWidth, mHeight);
                    }
                }

                break;

            case "Minimized":
                Logger.LogInfo("Minimizing Discord window");
                PublicApi.SetWindowState(windowHandle, WindowState.Minimized);
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                break;

            default: // Normal
                var useCustomSize = _settings.UseCustomSize.GetCurrentValue();
                if (useCustomSize)
                {
                    var width = _settings.WindowWidth.GetCurrentValue();
                    var height = _settings.WindowHeight.GetCurrentValue();
                    Logger.LogInfo("Setting Discord window size: {Width}x{Height}", width, height);
                    PublicApi.SetWindowSize(windowHandle, width, height);
                }

                break;
        }

        // Bring to foreground
        if (state != "Minimized" && _settings.BringToForeground.GetCurrentValue())
        {
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            Logger.LogInfo("Bringing Discord window to foreground");
            PublicApi.FocusWindow(windowHandle);
        }

        Logger.LogInfo("Discord window configuration completed");
    }
}