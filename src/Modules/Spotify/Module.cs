using System.Diagnostics;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.Spotify;

/// <summary>
///     Unified Spotify module that combines launcher and playback control functionality.
///     Supports OAuth authentication, playlist selection, and automatic playback on session start.
/// </summary>
public class Module : IModule, IAsyncDisposable
{
    private readonly IModuleLogger _logger;
    private readonly Settings _settings;

    private readonly ProcessService _processService;
    private readonly WindowService _windowService;
    private Process? _currentProcess;
    private bool _attachedToExisting;
    private bool _disposed;

    private readonly AuthService _authService;
    private readonly SpotifyApiService _apiService;
    private readonly PlaybackService _playbackService;

    public Module(
        IModuleLogger logger,
        Sdk.Http.IHttpClientFactory httpClientFactory,
        ISecureStorageService secureStorage,
        INotifier notifier,
        IAppDiscoveryService appDiscovery,
        ModuleDefinition definition)
    {
        _logger = logger;
        _settings = new Settings(appDiscovery);

        _processService = new ProcessService(logger);
        _windowService = new WindowService(logger);

        _authService = new AuthService(logger, httpClientFactory, secureStorage, definition, _settings, notifier);
        _apiService = new SpotifyApiService(httpClientFactory, definition, _authService, logger);
        _playbackService = new PlaybackService(logger, _settings, _authService, _apiService);
    }

    public IReadOnlyList<ISetting> GetSettings()
    {
        return _settings.GetAllSettings();
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return _settings.GetAllActions();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _settings.InitializeAsync();
        await _playbackService.InitializeAsync();
    }

    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        return _settings.ValidateAsync();
    }

    public async Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        await LaunchSpotifyAsync(cancellationToken);

        await _playbackService.OnSessionStartAsync(cancellationToken);
    }

    public async Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        await _playbackService.OnSessionEndAsync();

        await TerminateSpotifyAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _playbackService.Dispose();
        _authService.Dispose();
        _currentProcess?.Dispose();
        _currentProcess = null;

        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _playbackService.Dispose();
        _authService.Dispose();

        if (_currentProcess is { HasExited: false })
        {
            var lifecycleSetting = _settings.LifecycleMode.GetCurrentValue();
            var lifecycle = lifecycleSetting == "KeepRunning"
                ? ProcessLifecycleMode.KeepRunning
                : ProcessLifecycleMode.TerminateGraceful;

            try
            {
                await _processService.TerminateAsync(_currentProcess, lifecycle, _attachedToExisting)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to terminate process during async dispose: {Message}", ex.Message);
            }
        }

        _currentProcess?.Dispose();
        _currentProcess = null;

        GC.SuppressFinalize(this);
    }

    private async Task LaunchSpotifyAsync(CancellationToken cancellationToken)
    {
        var processConfig = BuildProcessConfig();

        _logger.LogInfo("Starting Spotify in {Mode} mode for {AppPath}",
            processConfig.StartMode, processConfig.ApplicationPath);

        var startResult = await _processService.StartAsync(processConfig).ConfigureAwait(false);
        _currentProcess = startResult.Process;
        _attachedToExisting = startResult.AttachedToExisting;

        if (_currentProcess == null)
        {
            _logger.LogError(null, "Failed to obtain process handle");
            return;
        }

        try
        {
            var windowConfig = BuildWindowConfig();
            await _windowService.ConfigureWindowAsync(_currentProcess, windowConfig, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            var fallback = await _processService.AttachExistingOnlyAsync(
                processConfig.ApplicationPath).ConfigureAwait(false);

            if (fallback == null)
            {
                _logger.LogWarning("Window did not appear in time. Skipping window configuration.");
                return;
            }

            _currentProcess = fallback;
            _attachedToExisting = true;

            var windowConfig = BuildWindowConfig();
            await _windowService.ConfigureWindowAsync(_currentProcess, windowConfig, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task TerminateSpotifyAsync()
    {
        if (_currentProcess == null || _currentProcess.HasExited)
        {
            _logger.LogDebug("Process already exited or not started");
            return;
        }

        var lifecycleSetting = _settings.LifecycleMode.GetCurrentValue();
        var lifecycle = ParseLifecycleMode(lifecycleSetting);

        _logger.LogInfo("Session ending. Lifecycle mode: {Mode}, Attached to existing: {Attached}",
            lifecycleSetting, _attachedToExisting);

        await _processService.TerminateAsync(_currentProcess, lifecycle, _attachedToExisting).ConfigureAwait(false);
    }

    private ProcessConfig BuildProcessConfig()
    {
        var appPath = _settings.ApplicationPath.GetCurrentValue();

        var startMode = _settings.ProcessMode.GetCurrentValue() switch
        {
            "AttachExisting" => ProcessStartMode.AttachExisting,
            "LaunchOrAttach" => ProcessStartMode.LaunchOrAttach,
            _ => ProcessStartMode.LaunchNew
        };

        var lifecycleMode = ParseLifecycleMode(_settings.LifecycleMode.GetCurrentValue());

        return new ProcessConfig(appPath, string.Empty, startMode, lifecycleMode, null);
    }

    private WindowConfig BuildWindowConfig()
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

        var bringToForeground = _settings.BringToForeground.GetCurrentValue();
        var timings = GetWindowConfigTimings();

        return new WindowConfig(
            state,
            useCustomSize,
            width,
            height,
            moveToMonitor,
            targetMonitorIndex,
            bringToForeground,
            timings.WaitForWindowTimeoutMs,
            timings.MoveDelayMs,
            timings.MaximizeSnapDelayMs,
            timings.FinalFocusDelayMs,
            timings.BannerDelayMs);
    }

    private static WindowConfigTimings GetWindowConfigTimings()
    {
        return new WindowConfigTimings(
            WaitForWindowTimeoutMs: 5000,
            MoveDelayMs: 1000,
            MaximizeSnapDelayMs: 1000,
            FinalFocusDelayMs: 1000,
            BannerDelayMs: 0
        );
    }

    private static ProcessLifecycleMode ParseLifecycleMode(string setting)
    {
        return setting switch
        {
            "KeepRunning" => ProcessLifecycleMode.KeepRunning,
            "TerminateForce" => ProcessLifecycleMode.TerminateForce,
            "TerminateOnEnd" => ProcessLifecycleMode.TerminateForce, // Backward compatibility
            _ => ProcessLifecycleMode.TerminateGraceful
        };
    }
}