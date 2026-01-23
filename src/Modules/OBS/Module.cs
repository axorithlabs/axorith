using System.Diagnostics;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.OBS;

/// <summary>
///     Module for launching OBS Studio and controlling streaming/recording via WebSocket.
///     Supports automatic start/stop of streaming, recording, and virtual camera.
/// </summary>
public class Module : IModule
{
    private const int MaxWebSocketConnectAttempts = 10;
    private const int InitialConnectDelayMs = 500;
    private const int MaxConnectDelayMs = 5000;
    private const int WindowTimeoutMs = 15000;

    private readonly IModuleLogger _logger;
    private readonly INotifier _notifier;
    private readonly Settings _settings;

    private readonly ProcessService _processService;
    private readonly WindowService _windowService;
    private Process? _currentProcess;
    private bool _attachedToExisting;

    private readonly ObsWebSocketService _webSocketService;

    public Module(
        IModuleLogger logger,
        IAppDiscoveryService appDiscovery,
        INotifier notifier,
        IPlatformProcessService processService,
        IPlatformWindowService windowService)
    {
        _logger = logger;
        _notifier = notifier;
        _settings = new Settings(appDiscovery);

        _processService = new ProcessService(logger, processService);
        _windowService = new WindowService(logger, windowService);

        _webSocketService = new ObsWebSocketService(logger, _settings);
    }

    public IReadOnlyList<ISetting> GetSettings()
    {
        return _settings.GetAllSettings();
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return _settings.GetAllActions();
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
        await LaunchObsAsync(cancellationToken);

        if (_settings.EnableWebSocket.GetCurrentValue())
        {
            if (await ConnectWithRetryAsync(cancellationToken))
            {
                var startAction = _settings.SessionStartAction.GetCurrentValue();
                await ExecuteActionAsync(startAction, cancellationToken);
            }
            else
            {
                _notifier.ShowToast(
                    "OBS: WebSocket connection failed. Enable WebSocket in OBS: Tools → WebSocket Server Settings.",
                    NotificationType.Error);
            }
        }
    }

    private async Task<bool> ConnectWithRetryAsync(CancellationToken cancellationToken)
    {
        var delay = InitialConnectDelayMs;

        for (var attempt = 1; attempt <= MaxWebSocketConnectAttempts; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            _logger.LogInfo("Attempting to connect to OBS WebSocket (attempt {Attempt}/{MaxAttempts})...",
                attempt, MaxWebSocketConnectAttempts);

            if (await _webSocketService.ConnectAsync(cancellationToken))
            {
                return true;
            }

            if (attempt < MaxWebSocketConnectAttempts)
            {
                await Task.Delay(delay, cancellationToken);
                delay = Math.Min(delay * 2, MaxConnectDelayMs);
            }
        }

        return false;
    }

    public async Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        if (_settings.EnableWebSocket.GetCurrentValue())
        {
            if (await _webSocketService.ConnectAsync(cancellationToken))
            {
                var endAction = _settings.SessionEndAction.GetCurrentValue();
                await ExecuteActionAsync(endAction, cancellationToken);

                if (endAction != Settings.ActionNone)
                {
                    await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
                }
            }

            await _webSocketService.DisconnectAsync();
        }

        await TerminateObsAsync();
    }

    public void Dispose()
    {
        try
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore errors during disposal
        }

        _webSocketService.Dispose();

        try
        {
            if (_currentProcess is { HasExited: false })
            {
                var lifecycle = _settings.LifecycleMode.GetCurrentValue() == "KeepRunning"
                    ? ProcessLifecycleMode.KeepRunning
                    : ProcessLifecycleMode.TerminateGraceful;

                _ = Task.Run(() => _processService.TerminateAsync(_currentProcess, lifecycle, _attachedToExisting));
            }
        }
        catch
        {
            // ignored
        }
        finally
        {
            _currentProcess?.Dispose();
            _currentProcess = null;
        }

        GC.SuppressFinalize(this);
    }

    private async Task DisconnectAsync()
    {
        if (_settings.EnableWebSocket.GetCurrentValue())
        {
            await _webSocketService.DisconnectAsync();
        }
    }

    private async Task ExecuteActionAsync(string action, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case Settings.ActionStartStreaming:
                await _webSocketService.StartStreamingAsync(cancellationToken);
                break;
            case Settings.ActionStartRecording:
                await _webSocketService.StartRecordingAsync(cancellationToken);
                break;
            case Settings.ActionStartBoth:
                await _webSocketService.StartStreamingAsync(cancellationToken);
                await _webSocketService.StartRecordingAsync(cancellationToken);
                break;
            case Settings.ActionStartVirtualCam:
                await _webSocketService.StartVirtualCameraAsync(cancellationToken);
                break;
            case Settings.ActionStopStreaming:
                await _webSocketService.StopStreamingAsync(cancellationToken);
                break;
            case Settings.ActionStopRecording:
                await _webSocketService.StopRecordingAsync(cancellationToken);
                break;
            case Settings.ActionStopBoth:
                await _webSocketService.StopStreamingAsync(cancellationToken);
                await _webSocketService.StopRecordingAsync(cancellationToken);
                break;
            case Settings.ActionStopVirtualCam:
                await _webSocketService.StopVirtualCameraAsync(cancellationToken);
                break;
            case Settings.ActionStopAll:
                await _webSocketService.StopStreamingAsync(cancellationToken);
                await _webSocketService.StopRecordingAsync(cancellationToken);
                await _webSocketService.StopVirtualCameraAsync(cancellationToken);
                break;
        }
    }

    private async Task LaunchObsAsync(CancellationToken cancellationToken)
    {
        var processConfig = BuildProcessConfig();

        _logger.LogInfo("Starting OBS Studio in {Mode} mode", processConfig.StartMode);

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
            _logger.LogWarning("Window did not appear in time");
        }
    }

    private async Task TerminateObsAsync()
    {
        if (_currentProcess == null || _currentProcess.HasExited)
        {
            return;
        }

        var lifecycle = ParseLifecycleMode(_settings.LifecycleMode.GetCurrentValue());
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

        // --disable-shutdown-check prevents the "Safe Mode" dialog after force kill
        const string obsArgs = "--disable-shutdown-check";

        return new ProcessConfig(appPath, obsArgs, startMode,
            ParseLifecycleMode(_settings.LifecycleMode.GetCurrentValue()), null);
    }

    private WindowConfig BuildWindowConfig()
    {
        var state = _settings.WindowState.GetCurrentValue();
        var useCustomSize = _settings.UseCustomSize.GetCurrentValue();
        int? width = null, height = null;

        if (useCustomSize && state == "Normal")
        {
            width = _settings.WindowWidth.GetCurrentValue();
            height = _settings.WindowHeight.GetCurrentValue();
        }

        var moveToMonitor = _settings.MoveToMonitor.GetCurrentValue();
        int? targetMonitorIndex = null;

        if (moveToMonitor && int.TryParse(_settings.TargetMonitor.GetCurrentValue(), out var idx))
        {
            targetMonitorIndex = idx;
        }

        return new WindowConfig(state, useCustomSize, width, height, moveToMonitor, targetMonitorIndex,
            _settings.BringToForeground.GetCurrentValue(), WindowTimeoutMs, 500, 500, 500);
    }

    private static ProcessLifecycleMode ParseLifecycleMode(string setting)
    {
        return setting switch
        {
            "KeepRunning" => ProcessLifecycleMode.KeepRunning,
            "TerminateForce" or "TerminateOnEnd" => ProcessLifecycleMode.TerminateForce,
            _ => ProcessLifecycleMode.TerminateGraceful
        };
    }
}