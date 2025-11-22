using System.Diagnostics;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.SpotifyLauncher;

public class Module : IModule
{
    private readonly IModuleLogger _logger;
    private readonly Settings _settings;
    private readonly ProcessService _process;
    private readonly WindowService _window;

    private Process? _currentProcess;
    private bool _attachedToExisting;

    public Module(IModuleLogger logger, IAppDiscoveryService appDiscovery)
    {
        _logger = logger;
        _settings = new Settings(appDiscovery);
        _process = new ProcessService(_logger);
        _window = new WindowService(_logger);
    }

    public IReadOnlyList<ISetting> GetSettings()
    {
        return _settings.GetSettings();
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return _settings.GetActions();
    }

    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        return _settings.ValidateAsync();
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return _settings.InitializeAsync();
    }

    public async Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        var mode = _settings.ProcessMode.GetCurrentValue();
        var spotifyPath = _settings.SpotifyPath.GetCurrentValue();

        _logger.LogInfo(
            "Starting Spotify in {Mode} mode at {Path}",
            mode, spotifyPath);

        var startMode = mode switch
        {
            "AttachExisting" => ProcessStartMode.AttachExisting,
            "LaunchOrAttach" => ProcessStartMode.LaunchOrAttach,
            _ => ProcessStartMode.LaunchNew
        };

        var lifecycleMode = _settings.LifecycleMode.GetCurrentValue() == "KeepRunning"
            ? ProcessLifecycleMode.KeepRunning
            : ProcessLifecycleMode.TerminateOnEnd;

        var processConfig = new ProcessConfig(
            spotifyPath, // Fixed: Pass the actual path
            string.Empty, // Spotify usually doesn't need args
            startMode,
            lifecycleMode,
            null // Use default working directory (exe folder)
        );

        var startResult = await _process.StartAsync(processConfig);
        _currentProcess = startResult.Process;
        _attachedToExisting = startResult.AttachedToExisting;

        if (_currentProcess == null)
        {
            _logger.LogError(null, "Failed to obtain Spotify process handle");
            return;
        }

        try
        {
            var windowConfig = CreateWindowConfigFromSettings();
            await _window.ConfigureWindowAsync(_currentProcess, windowConfig, cancellationToken);
        }
        catch (TimeoutException)
        {
            var fallback = await _process.AttachExistingOnlyAsync(spotifyPath, cancellationToken);
            if (fallback == null)
            {
                _logger.LogWarning(
                    "Spotify window did not appear in time. Skipping window configuration.");
                return;
            }

            _currentProcess = fallback;
            var windowConfig = CreateWindowConfigFromSettings();
            await _window.ConfigureWindowAsync(_currentProcess, windowConfig, cancellationToken);
        }
    }

    public async Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        if (_currentProcess == null || _currentProcess.HasExited)
        {
            _logger.LogDebug("Spotify process already exited or not started");
            return;
        }

        var lifecycleSetting = _settings.LifecycleMode.GetCurrentValue();
        var lifecycle = lifecycleSetting == "KeepRunning"
            ? ProcessLifecycleMode.KeepRunning
            : ProcessLifecycleMode.TerminateOnEnd;

        _logger.LogInfo("Session ending. Spotify lifecycle mode: {Mode}, Attached to existing: {Attached}",
            lifecycleSetting, _attachedToExisting);

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
                    : ProcessLifecycleMode.TerminateOnEnd;

                _ = Task.Run(() => _process.TerminateAsync(_currentProcess, lifecycle, _attachedToExisting));
            }
        }
        catch
        {
            // swallow in Dispose
        }
        finally
        {
            _currentProcess?.Dispose();
            _currentProcess = null;
        }

        GC.SuppressFinalize(this);
    }

    private WindowConfig CreateWindowConfigFromSettings()
    {
        var state = _settings.WindowState.GetCurrentValue();
        var useCustomSize = _settings.UseCustomSize.GetCurrentValue();
        int? width = null;
        int? height = null;

        if (useCustomSize && state == "Normal")
        {
            width = _settings.WindowWidth.GetCurrentValue();
            height = _settings.WindowHeight.GetCurrentValue();
        }

        var moveToMonitor = _settings.MoveToMonitor.GetCurrentValue();
        int? targetMonitorIndex = null;

        if (moveToMonitor)
        {
            var monitorKey = _settings.TargetMonitor.GetCurrentValue();
            if (!string.IsNullOrWhiteSpace(monitorKey) && int.TryParse(monitorKey, out var parsedIndex))
            {
                targetMonitorIndex = parsedIndex;
            }
        }

        // Spotify-specific behavior: delay move/maximize operations to avoid fighting with splash/banner.
        return new WindowConfig(
            state,
            useCustomSize,
            width,
            height,
            moveToMonitor,
            targetMonitorIndex,
            _settings.BringToForeground.GetCurrentValue(), // Always bring to foreground
            WaitForWindowTimeoutMs: 5000, // Increased timeout for Spotify splash screen
            MoveDelayMs: 1000,
            MaximizeSnapDelayMs: 1000,
            FinalFocusDelayMs: 1000,
            BannerDelayMs: 0);
    }
}