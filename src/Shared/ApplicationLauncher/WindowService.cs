using System.Diagnostics;
using Axorith.Sdk.Logging;
using Axorith.Shared.Platform;

namespace Axorith.Shared.ApplicationLauncher;

public sealed class WindowService(IModuleLogger logger, IPlatformWindowService windowService)
{
    public async Task ConfigureWindowAsync(Process process, WindowConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            if (config.BannerDelayMs > 0)
            {
                await Task.Delay(config.BannerDelayMs, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await windowService.WaitForWindowInitAsync(process, config.WaitForWindowTimeoutMs, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                logger.LogWarning(
                    "Process {ProcessName} window did not appear in time. Propagating timeout for fallback.",
                    process.ProcessName);
                throw;
            }

            if (process.MainWindowHandle == IntPtr.Zero)
            {
                logger.LogWarning("Process {ProcessName} has no main window. Skipping window configuration.",
                    process.ProcessName);
                return;
            }

            var windowHandle = process.MainWindowHandle;
            logger.LogDebug("Configuring window (Handle: {Handle})", windowHandle);

            var moveToMonitor = config.MoveToMonitor;
            var monitorIndex = config.TargetMonitorIndex;

            if (moveToMonitor && monitorIndex is { } idx)
            {
                if (config.MoveDelayMs > 0)
                {
                    await Task.Delay(config.MoveDelayMs, cancellationToken).ConfigureAwait(false);
                }

                logger.LogDebug("Moving window to monitor {MonitorIndex}", idx);
                windowService.MoveWindowToMonitor(windowHandle, idx);
            }

            switch (config.State)
            {
                case "Maximized":
                {
                    logger.LogDebug("Maximizing window");
                    windowService.SetWindowState(windowHandle, WindowState.Maximized);

                    if (config.MaximizeSnapDelayMs > 0)
                    {
                        await Task.Delay(config.MaximizeSnapDelayMs, cancellationToken).ConfigureAwait(false);
                    }

                    var current = windowService.GetWindowState(windowHandle);
                    if (current != WindowState.Maximized)
                    {
                        logger.LogWarning("Failed to maximize window");
                    }

                    if (moveToMonitor && monitorIndex is { } snapIndex)
                    {
                        var (mx, my, mWidth, mHeight) = windowService.GetMonitorBounds(snapIndex);
                        var (wx, wy, wWidth, wHeight) = windowService.GetWindowBounds(windowHandle);

                        if (wx != mx || wy != my || wWidth != mWidth || wHeight != mHeight)
                        {
                            logger.LogDebug(
                                "Window bounds {WX},{WY},{WWidth}x{WHeight} do not match monitor {MX},{MY},{MWidth}x{MHeight}. Snapping to monitor bounds.",
                                wx, wy, wWidth, wHeight, mx, my, mWidth, mHeight);

                            windowService.SetWindowPosition(windowHandle, mx, my);
                            windowService.SetWindowSize(windowHandle, mWidth, mHeight);
                        }
                    }

                    break;
                }

                case "Minimized":
                {
                    logger.LogDebug("Minimizing window");
                    windowService.SetWindowState(windowHandle, WindowState.Minimized);

                    if (config.MaximizeSnapDelayMs > 0)
                    {
                        await Task.Delay(config.MaximizeSnapDelayMs, cancellationToken).ConfigureAwait(false);
                    }

                    if (windowService.GetWindowState(windowHandle) != WindowState.Minimized)
                    {
                        logger.LogDebug("Re-applying minimize state after revert");
                        windowService.SetWindowState(windowHandle, WindowState.Minimized);
                    }

                    break;
                }

                default:
                {
                    if (config is { UseCustomSize: true, Width: { } width, Height: { } height })
                    {
                        logger.LogDebug("Setting custom window size: {Width}x{Height}", width, height);
                        windowService.SetWindowSize(windowHandle, width, height);
                    }

                    break;
                }
            }

            if (config.FinalFocusDelayMs > 0)
            {
                await Task.Delay(config.FinalFocusDelayMs, cancellationToken).ConfigureAwait(false);
            }

            if (config.BringToForeground && config.State != "Minimized")
            {
                logger.LogDebug("Bringing window to foreground");
                windowService.FocusWindow(windowHandle);
            }

            logger.LogInfo("Window configuration completed successfully for {ProcessName}", process.ProcessName);
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during window configuration");
        }
    }
}