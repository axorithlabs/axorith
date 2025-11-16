using System.Diagnostics;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Settings;

namespace Axorith.Module.ApplicationManager;

/// <summary>
///     Application Manager with window management capabilities.
///     Supports launching new processes, attaching to existing ones,
///     window state control, custom sizing, and lifecycle management.
/// </summary>
public class Module : IModule
{
    private readonly IModuleLogger _logger;
    private readonly Settings _settings;
    private readonly ProcessService _process;
    private readonly WindowService _window;

    private Process? _currentProcess;
    private bool _attachedToExisting;

    public Module(IModuleLogger logger)
    {
        _logger = logger;
        _settings = new Settings();
        _process = new ProcessService(_logger);
        _window = new WindowService(_logger, _settings);
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

        _logger.LogInfo("Starting in {Mode} mode for {AppPath}", mode, appPath);

        _currentProcess = mode switch
        {
            "AttachExisting" => await _process.AttachToExistingAsync(appPath),
            "LaunchOrAttach" => await AttachOrLaunchAsync(appPath, args, cancellationToken),
            _ => await _process.LaunchNewAsync(appPath, args)
        };

        if (_currentProcess == null)
        {
            _logger.LogError(null, "Failed to obtain process handle");
            return;
        }

        try
        {
            await _window.ConfigureWindowAsync(_currentProcess, cancellationToken);
        }
        catch (TimeoutException)
        {
            var fallback = await _process.AttachToExistingAsync(appPath);
            if (fallback == null)
            {
                _logger.LogWarning(
                    "Process {ProcessName} window did not appear in time. Skipping window configuration.",
                    _currentProcess.ProcessName);
                return;
            }

            _currentProcess = fallback;
            await _window.ConfigureWindowAsync(_currentProcess, cancellationToken);
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
            ? LifecycleMode.KeepRunning
            : LifecycleMode.TerminateOnEnd;

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
                    ? LifecycleMode.KeepRunning
                    : LifecycleMode.TerminateOnEnd;

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

    private async Task<Process?> AttachOrLaunchAsync(string appPath, string args, CancellationToken cancellationToken)
    {
        var existing = await _process.AttachToExistingAsync(appPath);
        if (existing != null)
        {
            _logger.LogInfo("Attached to existing process {ProcessName} (PID: {ProcessId})",
                existing.ProcessName, existing.Id);
            _attachedToExisting = true;
            return existing;
        }

        _logger.LogDebug("No existing process found, launching new one");
        var launched = await _process.LaunchNewAsync(appPath, args);
        _attachedToExisting = false;
        return launched;
    }
}