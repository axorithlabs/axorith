using System.Diagnostics;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.Steam;

/// <summary>
///     Module for launching Steam and optionally starting games.
/// </summary>
public class Module(
    IModuleLogger logger,
    INotifier notifier,
    IAppDiscoveryService appDiscovery,
    IPlatformProcessService processService,
    IPlatformWindowService windowService)
    : IModule
{
    private const int WindowTimeoutMs = 30000;

    private readonly Settings _settings = new(appDiscovery);

    private readonly ProcessService _processService = new(logger, processService);
    private readonly WindowService _windowService = new(logger, windowService);
    private Process? _currentProcess;
    private bool _attachedToExisting;

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
        var steamPath = _settings.ApplicationPath.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(steamPath))
        {
            logger.LogError(null, "Steam path not configured");
            notifier.ShowToast("Steam: Path not configured.", NotificationType.Error);
            return;
        }

        await LaunchSteamAsync(cancellationToken);

        var gameAppId = _settings.SelectedGame.GetCurrentValue();
        if (!string.IsNullOrWhiteSpace(gameAppId))
        {
            LaunchGame(gameAppId);
        }
    }

    public async Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        if (_currentProcess == null || _currentProcess.HasExited)
        {
            return;
        }

        var lifecycle = ParseLifecycleMode(_settings.LifecycleMode.GetCurrentValue());
        await _processService.TerminateAsync(_currentProcess, lifecycle, _attachedToExisting).ConfigureAwait(false);
    }

    public void Dispose()
    {
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

    private async Task LaunchSteamAsync(CancellationToken cancellationToken)
    {
        var processConfig = BuildProcessConfig();
        logger.LogInfo("Starting Steam in {Mode} mode", processConfig.StartMode);

        var startResult = await _processService.StartAsync(processConfig).ConfigureAwait(false);
        _currentProcess = startResult.Process;
        _attachedToExisting = startResult.AttachedToExisting;

        if (_currentProcess == null)
        {
            logger.LogError(null, "Failed to obtain Steam process handle");
            notifier.ShowToast("Steam: Failed to launch.", NotificationType.Error);
            return;
        }

        logger.LogInfo("Steam process started (PID: {Pid}, Attached: {Attached})",
            _currentProcess.Id, _attachedToExisting);

        try
        {
            var windowConfig = BuildWindowConfig();
            await _windowService.ConfigureWindowAsync(_currentProcess, windowConfig, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Steam window did not appear in time");
        }
        catch (InvalidOperationException)
        {
            // Process exited
        }
    }

    private void LaunchGame(string appId)
    {
        try
        {
            var steamUrl = $"steam://rungameid/{appId}";
            logger.LogInfo("Launching game with AppID {AppId}", appId);

            Process.Start(new ProcessStartInfo(steamUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to launch game {AppId}", appId);
            notifier.ShowToast("Steam: Failed to launch game.", NotificationType.Error);
        }
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

        return new ProcessConfig(appPath, string.Empty, startMode,
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
            _settings.BringToForeground.GetCurrentValue(), WindowTimeoutMs, 500, 1000, 500);
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