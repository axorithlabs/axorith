using System.Diagnostics;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;

namespace Axorith.Module.ApplicationLauncher;

/// <summary>
///     Application Launcher with window management capabilities.
///     Supports launching new processes, attaching to existing ones,
///     window state control, custom sizing, and lifecycle management.
/// </summary>
public class Module : IModule
{
    private readonly IModuleLogger _logger;
    private readonly Settings _settings;
    private readonly Shared.ApplicationLauncher.ProcessService _process;
    private readonly Shared.ApplicationLauncher.WindowService _window;

    private Process? _currentProcess;
    private bool _attachedToExisting;

    public Module(IModuleLogger logger)
    {
        _logger = logger;
        _settings = new Settings();
        _process = new Shared.ApplicationLauncher.ProcessService(_logger);
        _window = new Shared.ApplicationLauncher.WindowService(_logger);
    }

    public IReadOnlyList<ISetting> GetSettings()
    {
        return _settings.GetSettings();
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return [];
    }

    public Type? CustomSettingsViewType => null;

    public object? GetSettingsViewModel()
    {
        return null;
    }

    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        return _settings.ValidateAsync(cancellationToken);
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        var mode = _settings.ProcessMode.GetCurrentValue();
        var appPath = _settings.ApplicationPath.GetCurrentValue();
        var args = _settings.ApplicationArgs.GetCurrentValue();

        var workingDirectory = _settings.UseCustomWorkingDirectory.GetCurrentValue()
            ? _settings.WorkingDirectory.GetCurrentValue()
            : null;

        _logger.LogInfo("Starting in {Mode} mode for {AppPath} (WorkingDir: {WorkingDirectory})", mode, appPath,
            string.IsNullOrWhiteSpace(workingDirectory) ? "<default>" : workingDirectory);

        var startMode = mode switch
        {
            "AttachExisting" => ProcessStartMode.AttachExisting,
            "LaunchOrAttach" => ProcessStartMode.LaunchOrAttach,
            _ => ProcessStartMode.LaunchNew
        };

        var lifecycleMode = _settings.LifecycleMode.GetCurrentValue() == "KeepRunning"
            ? ProcessLifecycleMode.KeepRunning
            : ProcessLifecycleMode.TerminateOnEnd;

        var processConfig = new ProcessConfig(appPath, args, startMode, lifecycleMode, workingDirectory);

        var startResult = await _process.StartAsync(processConfig, cancellationToken);
        _currentProcess = startResult.Process;
        _attachedToExisting = startResult.AttachedToExisting;

        if (_currentProcess == null)
        {
            _logger.LogError(null, "Failed to obtain process handle");
            return;
        }

        try
        {
            var windowConfig = CreateWindowConfigFromSettings();
            await _window.ConfigureWindowAsync(_currentProcess, windowConfig, cancellationToken);
        }
        catch (TimeoutException)
        {
            var fallback = await _process.AttachExistingOnlyAsync(appPath, cancellationToken);
            if (fallback == null)
            {
                _logger.LogWarning(
                    "Process {ProcessName} window did not appear in time. Skipping window configuration.",
                    _currentProcess.ProcessName);
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
            _logger.LogDebug("Process already exited or not started");
            return;
        }

        var lifecycleSetting = _settings.LifecycleMode.GetCurrentValue();
        var lifecycle = lifecycleSetting == "KeepRunning"
            ? ProcessLifecycleMode.KeepRunning
            : ProcessLifecycleMode.TerminateOnEnd;

        _logger.LogInfo("Session ending. Lifecycle mode: {Mode}, Attached to existing: {Attached}",
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

                _process.TerminateAsync(_currentProcess, lifecycle, _attachedToExisting)
                    .GetAwaiter().GetResult();
            }
        }
        catch
        {
            // Swallow exceptions in Dispose.
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
                targetMonitorIndex = parsedIndex;
        }

        var bringToForeground = _settings.BringToForeground.GetCurrentValue();

        return new WindowConfig(
            state,
            useCustomSize,
            width,
            height,
            moveToMonitor,
            targetMonitorIndex,
            bringToForeground);
    }
}