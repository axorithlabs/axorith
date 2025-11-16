using System.Diagnostics;
using Axorith.Sdk.Logging;
using Axorith.Shared.Platform;
using Axorith.Shared.Platform.Windows;

namespace Axorith.Module.ApplicationManager;

/// <summary>
///     Service responsible for window management via PublicApi and window handles,
///     using Settings as the source of configuration.
/// </summary>
internal sealed class WindowService(IModuleLogger logger, Settings settings)
{
    public async Task ConfigureWindowAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            try
            {
                await PublicApi.WaitForWindowInitAsync(process, 7000, cancellationToken);
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

            var stateStr = settings.WindowState.GetCurrentValue();
            var monitorIndex = settings.MonitorIndex.GetCurrentValue();

            switch (monitorIndex)
            {
                case -1:
                    break;
                default:
                {
                    await Task.Delay(300, cancellationToken);
                    logger.LogDebug("Moving window to monitor {MonitorIndex}", monitorIndex);
                    PublicApi.MoveWindowToMonitor(windowHandle, monitorIndex);
                    break;
                }
            }

            switch (stateStr)
            {
                case "Maximized":
                {
                    logger.LogDebug("Maximizing window");
                    PublicApi.SetWindowState(windowHandle, WindowState.Maximized);
                    
                    await Task.Delay(250, cancellationToken);
                    var current = PublicApi.GetWindowState(windowHandle);
                    if (current != WindowState.Maximized)
                        logger.LogWarning("Failed to maximize window");

                    // Some Electron apps (VS Code, Spotify) report Maximized but keep
                    // their window slightly offset or sized smaller than the monitor.
                    // As a final corrective step, snap the window bounds exactly to
                    // the target monitor if we have a valid monitor index.
                    var (mx, my, mWidth, mHeight) = PublicApi.GetMonitorBounds(monitorIndex);
                    var (wx, wy, wWidth, wHeight) = PublicApi.GetWindowBounds(windowHandle);

                    if (wx != mx || wy != my || wWidth != mWidth || wHeight != mHeight)
                    {
                        logger.LogDebug(
                            "Window bounds {WX},{WY},{WWidth}x{WHeight} do not match monitor {MX},{MY},{MWidth}x{MHeight}. Snapping to monitor bounds.",
                            wx, wy, wWidth, wHeight, mx, my, mWidth, mHeight);

                        PublicApi.SetWindowPosition(windowHandle, mx, my);
                        PublicApi.SetWindowSize(windowHandle, mWidth, mHeight);
                    }

                    break;
                }
                case "Minimized":
                {
                    logger.LogDebug("Minimizing window");
                    PublicApi.SetWindowState(windowHandle, WindowState.Minimized);
                    await Task.Delay(250, cancellationToken);
                    if (PublicApi.GetWindowState(windowHandle) != WindowState.Minimized)
                    {
                        logger.LogDebug("Re-applying minimize state after revert");
                        PublicApi.SetWindowState(windowHandle, WindowState.Minimized);
                    }

                    break;
                }
                default:
                {
                    if (settings.UseCustomSize.GetCurrentValue())
                    {
                        var width = settings.WindowWidth.GetCurrentValue();
                        var height = settings.WindowHeight.GetCurrentValue();
                        logger.LogDebug("Setting custom window size: {Width}x{Height}", width, height);
                        PublicApi.SetWindowSize(windowHandle, width, height);
                    }

                    break;
                }
            }

            await Task.Delay(250, cancellationToken);

            if (settings.BringToForeground.GetCurrentValue() && stateStr != "Minimized")
            {
                logger.LogDebug("Bringing window to foreground");
                PublicApi.FocusWindow(windowHandle);
            }

            logger.LogInfo("Window configuration completed successfully for {ProcessName}",
                process.ProcessName);
        }
        catch (TimeoutException)
        {
            // Already logged inside; just rethrow so the caller can decide on fallback.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during window configuration");
        }
    }
}