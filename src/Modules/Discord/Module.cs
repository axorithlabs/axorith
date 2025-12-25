using System.Diagnostics;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.Discord;

/// <summary>
///     Module for launching and managing Discord application.
/// </summary>
public class Module(IModuleLogger logger, IAppDiscoveryService appDiscovery) : IModule
{
    private const int MaxWindowWaitMs = 20000;
    private const int WindowCheckIntervalMs = 500;
    private const int PostWindowReadyDelayMs = 1500;

    /// <summary>
    ///     Window titles that indicate Discord is still loading/updating.
    /// </summary>
    private static readonly string[] LoadingWindowTitles = ["Updating", "Starting", "Loading", "Checking"];

    private readonly Settings _settings = new(appDiscovery);

    private readonly ProcessService _processService = new(logger);
    private readonly WindowService _windowService = new(logger);
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
        await LaunchDiscordAsync(cancellationToken);
    }

    public async Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        await TerminateDiscordAsync();
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

    private async Task LaunchDiscordAsync(CancellationToken cancellationToken)
    {
        var processConfig = BuildProcessConfig();

        logger.LogInfo("Starting Discord in {Mode} mode", processConfig.StartMode);

        var startResult = await _processService.StartAsync(processConfig).ConfigureAwait(false);
        _currentProcess = startResult.Process;
        _attachedToExisting = startResult.AttachedToExisting;

        if (_currentProcess == null)
        {
            logger.LogError(null, "Failed to obtain process handle");
            return;
        }

        try
        {
            await WaitForDiscordMainWindowAsync(cancellationToken).ConfigureAwait(false);

            var windowConfig = BuildWindowConfig();
            await _windowService.ConfigureWindowAsync(_currentProcess, windowConfig, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Window did not appear in time");
        }
        catch (InvalidOperationException)
        {
            // Process exited during window configuration - ignore
        }
    }

    private async Task TerminateDiscordAsync()
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
            _settings.BringToForeground.GetCurrentValue(), 20000, 500, 1000, 500);
    }

    private async Task WaitForDiscordMainWindowAsync(CancellationToken cancellationToken)
    {
        if (_currentProcess == null || _currentProcess.HasExited)
        {
            return;
        }

        var startTime = DateTime.UtcNow;

        while ((DateTime.UtcNow - startTime).TotalMilliseconds < MaxWindowWaitMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_currentProcess.HasExited)
            {
                return;
            }

            _currentProcess.Refresh();

            if (_currentProcess.MainWindowHandle != IntPtr.Zero)
            {
                var title = _currentProcess.MainWindowTitle;
                if (!string.IsNullOrWhiteSpace(title) && !IsLoadingTitle(title))
                {
                    await Task.Delay(PostWindowReadyDelayMs, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            await Task.Delay(WindowCheckIntervalMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsLoadingTitle(string title)
    {
        return LoadingWindowTitles.Any(loading =>
            title.Contains(loading, StringComparison.OrdinalIgnoreCase));
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