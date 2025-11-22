using System.Diagnostics;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.VSCodeLauncher;

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
        var codePath = _settings.CodePath.GetCurrentValue();
        var projectPath = _settings.ProjectPath.GetCurrentValue();
        var userArgs = _settings.ApplicationArgs.GetCurrentValue();

        var args = userArgs;
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            var safePath = projectPath.Contains(' ') ? $"\"{projectPath}\"" : projectPath;
            args = string.IsNullOrWhiteSpace(args) ? safePath : $"{args} {safePath}";
        }

        _logger.LogInfo("Starting VS Code in {Mode} mode at {Path} with args: {Args}", mode, codePath, args);

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
            codePath,
            args,
            startMode,
            lifecycleMode,
            null
        );

        var startResult = await _process.StartAsync(processConfig);
        _currentProcess = startResult.Process;
        _attachedToExisting = startResult.AttachedToExisting;

        if (_currentProcess == null)
        {
            _logger.LogError(null, "Failed to obtain VS Code process handle");
            return;
        }

        try
        {
            var windowConfig = CreateWindowConfigFromSettings();
            await _window.ConfigureWindowAsync(_currentProcess, windowConfig, cancellationToken);
        }
        catch (TimeoutException)
        {
            var fallback = await _process.AttachExistingOnlyAsync(codePath, cancellationToken);
            if (fallback == null)
            {
                _logger.LogWarning("VS Code window did not appear in time. Skipping window configuration.");
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
            return;
        }

        var lifecycleSetting = _settings.LifecycleMode.GetCurrentValue();
        var lifecycle = lifecycleSetting == "KeepRunning"
            ? ProcessLifecycleMode.KeepRunning
            : ProcessLifecycleMode.TerminateOnEnd;

        _logger.LogInfo("Session ending. VS Code lifecycle: {Mode}", lifecycleSetting);

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
            // ignore
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

        var bringToForeground = _settings.BringToForeground.GetCurrentValue();

        return new WindowConfig(
            state,
            useCustomSize,
            width,
            height,
            moveToMonitor,
            targetMonitorIndex,
            bringToForeground,
            WaitForWindowTimeoutMs: 10000, // Electron apps can be slow
            MoveDelayMs: 1000,
            MaximizeSnapDelayMs: 500,
            FinalFocusDelayMs: 500,
            BannerDelayMs: 0);
    }
}