using System.Diagnostics;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.DiscordLauncher;

public class Module : IModule
{
    private readonly IModuleLogger _logger;
    private readonly Settings _settings;
    private readonly ProcessService _process;

    private Process? _currentProcess;
    private bool _attachedToExisting;

    public Module(IModuleLogger logger, IAppDiscoveryService appDiscovery)
    {
        _logger = logger;
        _settings = new Settings(appDiscovery);
        _process = new ProcessService(_logger);
    }

    public IReadOnlyList<ISetting> GetSettings()
    {
        return _settings.GetSettings();
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return _settings.GetActions();
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return _settings.InitializeAsync();
    }

    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        return _settings.ValidateAsync();
    }

    public async Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        var mode = _settings.ProcessMode.GetCurrentValue();
        var discordPath = _settings.DiscordPath.GetCurrentValue();

        _logger.LogInfo("Starting Discord in {Mode} mode at {Path}", mode, discordPath);

        var startMode = mode switch
        {
            "AttachExisting" => ProcessStartMode.AttachExisting,
            "LaunchOrAttach" => ProcessStartMode.LaunchOrAttach,
            _ => ProcessStartMode.LaunchNew
        };

        var lifecycleMode = _settings.LifecycleMode.GetCurrentValue() == "KeepRunning"
            ? ProcessLifecycleMode.KeepRunning
            : ProcessLifecycleMode.TerminateGraceful;

        var processConfig = new ProcessConfig(
            discordPath,
            string.Empty, // Discord doesn't need args
            startMode,
            lifecycleMode,
            null
        );

        var startResult = await _process.StartAsync(processConfig);
        _currentProcess = startResult.Process;
        _attachedToExisting = startResult.AttachedToExisting;

        if (_currentProcess == null)
        {
            _logger.LogError(null, "Failed to obtain Discord process handle");
            return;
        }

        await WaitForDiscordMainWindowAsync(cancellationToken);

        await ConfigureDiscordWindowAsync(cancellationToken);
    }

    private async Task WaitForDiscordMainWindowAsync(CancellationToken cancellationToken)
    {
        if (_currentProcess == null || _currentProcess.HasExited)
        {
            return;
        }

        _logger.LogInfo("Waiting for Discord main window (skipping splash screen)...");

        var startTime = DateTime.Now;
        const int maxWaitMs = 20000;
        const int checkIntervalMs = 500;

        while ((DateTime.Now - startTime).TotalMilliseconds < maxWaitMs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_currentProcess.HasExited)
            {
                _logger.LogWarning("Discord process exited while waiting for main window");
                return;
            }

            _currentProcess.Refresh();

            if (_currentProcess.MainWindowHandle != IntPtr.Zero)
            {
                var title = _currentProcess.MainWindowTitle;
                
                if (!string.IsNullOrWhiteSpace(title) && 
                    !title.Equals("Discord", StringComparison.OrdinalIgnoreCase) &&
                    !title.Contains("Updating", StringComparison.OrdinalIgnoreCase) &&
                    !title.Contains("Starting", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInfo("Discord main window detected with title: {Title}", title);
                    
                    await Task.Delay(1500, cancellationToken);
                    return;
                }
            }

            await Task.Delay(checkIntervalMs, cancellationToken);
        }

        _logger.LogWarning("Discord main window did not appear with expected title within timeout");
    }

    private async Task ConfigureDiscordWindowAsync(CancellationToken cancellationToken)
    {
        if (_currentProcess == null || _currentProcess.HasExited)
        {
            _logger.LogWarning("Discord process is null or exited, cannot configure window");
            return;
        }

        _currentProcess.Refresh();

        if (_currentProcess.MainWindowHandle == IntPtr.Zero)
        {
            _logger.LogWarning("Discord has no main window handle");
            return;
        }

        var windowHandle = _currentProcess.MainWindowHandle;
        _logger.LogInfo("Configuring Discord window (Handle: {Handle}, Title: {Title})", 
            windowHandle, _currentProcess.MainWindowTitle);

        var state = _settings.WindowState.GetCurrentValue();
        var moveToMonitor = _settings.MoveToMonitor.GetCurrentValue();
        var monitorIndex = _settings.TargetMonitor.GetCurrentValue();

        if (moveToMonitor && !string.IsNullOrWhiteSpace(monitorIndex) && int.TryParse(monitorIndex, out var idx))
        {
            _logger.LogInfo("Moving Discord window to monitor {MonitorIndex}", idx);
            await Task.Delay(500, cancellationToken);
            PublicApi.MoveWindowToMonitor(windowHandle, idx);
            await Task.Delay(500, cancellationToken);
        }

        switch (state)
        {
            case "Maximized":
                _logger.LogInfo("Maximizing Discord window");
                PublicApi.SetWindowState(windowHandle, WindowState.Maximized);
                await Task.Delay(1000, cancellationToken);

                if (moveToMonitor && !string.IsNullOrWhiteSpace(monitorIndex) && int.TryParse(monitorIndex, out var snapIdx))
                {
                    var (mx, my, mWidth, mHeight) = PublicApi.GetMonitorBounds(snapIdx);
                    var (wx, wy, wWidth, wHeight) = PublicApi.GetWindowBounds(windowHandle);

                    if (wx != mx || wy != my || wWidth != mWidth || wHeight != mHeight)
                    {
                        _logger.LogInfo("Snapping Discord window to monitor bounds");
                        PublicApi.SetWindowPosition(windowHandle, mx, my);
                        PublicApi.SetWindowSize(windowHandle, mWidth, mHeight);
                    }
                }
                break;

            case "Minimized":
                _logger.LogInfo("Minimizing Discord window");
                PublicApi.SetWindowState(windowHandle, WindowState.Minimized);
                await Task.Delay(500, cancellationToken);
                break;

            default: // Normal
                var useCustomSize = _settings.UseCustomSize.GetCurrentValue();
                if (useCustomSize)
                {
                    var width = _settings.WindowWidth.GetCurrentValue();
                    var height = _settings.WindowHeight.GetCurrentValue();
                    _logger.LogInfo("Setting Discord window size: {Width}x{Height}", width, height);
                    PublicApi.SetWindowSize(windowHandle, width, height);
                }
                break;
        }

        // Bring to foreground
        if (state != "Minimized" && _settings.BringToForeground.GetCurrentValue())
        {
            await Task.Delay(500, cancellationToken);
            _logger.LogInfo("Bringing Discord window to foreground");
            PublicApi.FocusWindow(windowHandle);
        }

        _logger.LogInfo("Discord window configuration completed");
    }

    public async Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        if (_currentProcess == null || _currentProcess.HasExited)
        {
            return;
        }

        var lifecycleSetting = _settings.LifecycleMode.GetCurrentValue();
        var lifecycle = lifecycleSetting switch
        {
            "KeepRunning" => ProcessLifecycleMode.KeepRunning,
            "TerminateForce" => ProcessLifecycleMode.TerminateForce,
            "TerminateOnEnd" => ProcessLifecycleMode.TerminateForce, // Backward compatibility
            _ => ProcessLifecycleMode.TerminateGraceful // Default to graceful
        };

        _logger.LogInfo("Session ending. Discord lifecycle: {Mode}", lifecycleSetting);

        await _process.TerminateAsync(_currentProcess, lifecycle, _attachedToExisting);
    }

    public void Dispose()
    {
        try
        {
            if (_currentProcess is { HasExited: false })
            {
                var lifecycleSetting = _settings.LifecycleMode.GetCurrentValue();
                var lifecycle = lifecycleSetting == "KeepRunning"
                    ? ProcessLifecycleMode.KeepRunning
                    : ProcessLifecycleMode.TerminateGraceful;

                _ = Task.Run(() => _process.TerminateAsync(_currentProcess, lifecycle, _attachedToExisting));
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            _currentProcess?.Dispose();
            _currentProcess = null;
        }

        GC.SuppressFinalize(this);
    }
}