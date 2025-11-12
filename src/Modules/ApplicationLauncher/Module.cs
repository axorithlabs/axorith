using System.Diagnostics;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;

namespace Axorith.Module.ApplicationLauncher;

public enum ProcessMode
{
    LaunchNew,
    AttachExisting,
    LaunchOrAttach
}

public enum LifecycleMode
{
    TerminateOnEnd,
    KeepRunning
}

/// <summary>
///     Advanced application launcher with window management capabilities.
///     Supports launching new processes, attaching to existing ones,
///     window state control, custom sizing, and lifecycle management.
/// </summary>
public class Module : IModule
{
    private readonly IModuleLogger _logger;

    private readonly Setting<string> _applicationPath;
    private readonly Setting<string> _applicationArgs;
    private readonly Setting<string> _processMode;
    private readonly Setting<int> _monitorIndex;
    private readonly Setting<string> _windowState;
    private readonly Setting<bool> _useCustomSize;
    private readonly Setting<int> _windowWidth;
    private readonly Setting<int> _windowHeight;
    private readonly Setting<string> _lifecycleMode;
    private readonly Setting<bool> _bringToForeground;

    private Process? _currentProcess;
    private bool _attachedToExisting;

    public Module(IModuleLogger logger)
    {
        _logger = logger;

        _processMode = Setting.AsChoice(
            key: "ProcessMode",
            label: "Process Mode",
            defaultValue: "LaunchNew",
            initialChoices: new[]
            {
                new KeyValuePair<string, string>("LaunchNew", "Launch New Process"),
                new KeyValuePair<string, string>("AttachExisting", "Attach to Existing"),
                new KeyValuePair<string, string>("LaunchOrAttach", "Launch or Attach")
            },
            description: "How to handle the application process."
        );

        _applicationPath = Setting.AsFilePicker(
            key: "ApplicationPath",
            label: "Application Path",
            description: "Path to the application executable. Used for launching new processes or finding existing ones.",
            defaultValue: @"C:\Windows\notepad.exe",
            filter: "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
        );

        _applicationArgs = Setting.AsText(
            key: "ApplicationArgs",
            label: "Launch Arguments",
            description: "Command-line arguments to pass when launching a new process.",
            defaultValue: ""
        );

        _monitorIndex = Setting.AsInt(
            key: "MonitorIndex",
            label: "Target Monitor",
            description: "Monitor index (0-based) where the window should be placed. Use 0 for primary monitor.",
            defaultValue: 0
        );

        _windowState = Setting.AsChoice(
            key: "WindowState",
            label: "Window State",
            defaultValue: "Normal",
            initialChoices: new[]
            {
                new KeyValuePair<string, string>("Normal", "Normal"),
                new KeyValuePair<string, string>("Maximized", "Maximized"),
                new KeyValuePair<string, string>("Minimized", "Minimized")
            },
            description: "Desired window state after session starts."
        );

        _useCustomSize = Setting.AsCheckbox(
            key: "UseCustomSize",
            label: "Use Custom Window Size",
            defaultValue: false,
            description: "Enable custom window dimensions. Requires Normal window state."
        );

        _windowWidth = Setting.AsInt(
            key: "WindowWidth",
            label: "Window Width",
            description: "Custom window width in pixels.",
            defaultValue: 1280,
            isVisible: false  // Initially hidden until UseCustomSize is enabled
        );

        _windowHeight = Setting.AsInt(
            key: "WindowHeight",
            label: "Window Height",
            description: "Custom window height in pixels.",
            defaultValue: 720,
            isVisible: false  // Initially hidden until UseCustomSize is enabled
        );

        _lifecycleMode = Setting.AsChoice(
            key: "LifecycleMode",
            label: "Process Lifecycle",
            defaultValue: "TerminateOnEnd",
            initialChoices: new[]
            {
                new KeyValuePair<string, string>("TerminateOnEnd", "Terminate on Session End"),
                new KeyValuePair<string, string>("KeepRunning", "Keep Running")
            },
            description: "What happens to the process when session ends."
        );

        _bringToForeground = Setting.AsCheckbox(
            key: "BringToForeground",
            label: "Bring to Foreground",
            defaultValue: true,
            description: "Automatically bring the window to foreground after setup."
        );

        // Setup reactive visibility in constructor (before GetSettings is called)
        SetupReactiveVisibility();
    }

    private void SetupReactiveVisibility()
    {
        // React to UseCustomSize changes (respect current WindowState)
        _useCustomSize.Value.Subscribe(useCustom =>
        {
            var state = _windowState.GetCurrentValue();
            var isNormal = state == "Normal";
            _windowWidth.SetVisibility(isNormal && useCustom);
            _windowHeight.SetVisibility(isNormal && useCustom);
        });

        // React to WindowState changes
        _windowState.Value.Subscribe(state =>
        {
            var isNormal = state == "Normal";
            var isMinimized = state == "Minimized";
            
            // UseCustomSize only visible for Normal state
            _useCustomSize.SetVisibility(isNormal);
            
            // BringToForeground hidden when Minimized
            _bringToForeground.SetVisibility(!isMinimized);
            
            // Width/Height visibility depends on both WindowState AND UseCustomSize
            var useCustom = _useCustomSize.GetCurrentValue();
            _windowWidth.SetVisibility(isNormal && useCustom);
            _windowHeight.SetVisibility(isNormal && useCustom);
        });

        // React to ProcessMode changes
        _processMode.Value.Subscribe(mode =>
        {
            // ApplicationArgs only visible when launching (not when attaching to existing)
            var showArgs = mode == "LaunchNew" || mode == "LaunchOrAttach";
            _applicationArgs.SetVisibility(showArgs);
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<ISetting> GetSettings()
    {
        return
        [
            _processMode,
            _applicationPath,
            _applicationArgs,
            _monitorIndex,
            _windowState,
            _useCustomSize,
            _windowWidth,
            _windowHeight,
            _lifecycleMode,
            _bringToForeground
        ];
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return [];
    }

    /// <inheritdoc />
    public Type? CustomSettingsViewType => null;

    /// <inheritdoc />
    public object? GetSettingsViewModel()
    {
        // This module uses auto-generated UI, so this method returns null.
        return null;
    }

    /// <inheritdoc />
    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_applicationPath.GetCurrentValue()))
            return Task.FromResult(ValidationResult.Fail("'Application Path' is required."));

        var mode = _processMode.GetCurrentValue();
        if (mode == "LaunchNew" && !File.Exists(_applicationPath.GetCurrentValue()))
            return Task.FromResult(ValidationResult.Fail($"File not found at '{_applicationPath.GetCurrentValue()}'."));

        if (_monitorIndex.GetCurrentValue() < 0)
            return Task.FromResult(ValidationResult.Fail("'Monitor Index' must be a non-negative number."));

        if (_useCustomSize.GetCurrentValue())
        {
            if (_windowWidth.GetCurrentValue() < 100)
                return Task.FromResult(ValidationResult.Fail("'Window Width' must be at least 100 pixels."));
            if (_windowHeight.GetCurrentValue() < 100)
                return Task.FromResult(ValidationResult.Fail("'Window Height' must be at least 100 pixels."));
        }

        return Task.FromResult(ValidationResult.Success);
    }

    /// <inheritdoc />
    public async Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        var mode = _processMode.GetCurrentValue();
        var appPath = _applicationPath.GetCurrentValue();

        _logger.LogInfo("ApplicationLauncher starting in {Mode} mode for {AppPath}", mode, appPath);

        // Step 1: Get or create process
        switch (mode)
        {
            case "AttachExisting":
                _currentProcess = await AttachToExistingProcessAsync(appPath);
                if (_currentProcess == null)
                {
                    _logger.LogError(null, "Failed to find existing process for {AppPath}", appPath);
                    throw new InvalidOperationException($"No running process found for {appPath}");
                }
                _attachedToExisting = true;
                break;

            case "LaunchOrAttach":
                _currentProcess = await AttachToExistingProcessAsync(appPath);
                if (_currentProcess != null)
                {
                    _logger.LogInfo("Attached to existing process {ProcessName} (PID: {ProcessId})",
                        _currentProcess.ProcessName, _currentProcess.Id);
                    _attachedToExisting = true;
                }
                else
                {
                    _logger.LogDebug("No existing process found, launching new one");
                    _currentProcess = await LaunchNewProcessAsync(appPath, _applicationArgs.GetCurrentValue());
                    _attachedToExisting = false;
                }
                break;

            case "LaunchNew":
            default:
                _currentProcess = await LaunchNewProcessAsync(appPath, _applicationArgs.GetCurrentValue());
                _attachedToExisting = false;
                break;
        }

        if (_currentProcess == null)
        {
            _logger.LogError(null, "Failed to obtain process handle");
            return;
        }

        // Step 2: Wait for window and configure it
        await ConfigureWindowAsync(_currentProcess, cancellationToken);
    }

    private Task<Process?> LaunchNewProcessAsync(string path, string args)
    {
        try
        {
            _logger.LogDebug("Launching process: {Path} {Args}", path, args);
            
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = args,
                    UseShellExecute = true
                }
            };

            if (!process.Start())
            {
                _logger.LogError(null, "Process.Start() returned false");
                return Task.FromResult<Process?>(null);
            }

            _logger.LogInfo("Process {ProcessName} (PID: {ProcessId}) launched successfully",
                process.ProcessName, process.Id);
            
            return Task.FromResult<Process?>(process);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch process: {Path}", path);
            return Task.FromResult<Process?>(null);
        }
    }

    private Task<Process?> AttachToExistingProcessAsync(string path)
    {
        try
        {
            _logger.LogDebug("Searching for existing process: {Path}", path);
            
            var processes = PublicApi.FindProcesses(path);
            
            if (processes.Count == 0)
            {
                _logger.LogDebug("No existing process found for {Path}", path);
                return Task.FromResult<Process?>(null);
            }

            // Prefer process with window
            var processWithWindow = processes.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
            var selectedProcess = processWithWindow ?? processes.First();

            _logger.LogInfo("Found existing process {ProcessName} (PID: {ProcessId})",
                selectedProcess.ProcessName, selectedProcess.Id);
            
            return Task.FromResult<Process?>(selectedProcess);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while searching for existing process");
            return Task.FromResult<Process?>(null);
        }
    }

    private async Task ConfigureWindowAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            // Wait for window to appear
            await PublicApi.WaitForWindowInitAsync(process, 5000, cancellationToken);

            if (process.MainWindowHandle == IntPtr.Zero)
            {
                _logger.LogWarning("Process {ProcessName} has no main window. Skipping window configuration.",
                    process.ProcessName);
                return;
            }

            var windowHandle = process.MainWindowHandle;
            _logger.LogDebug("Configuring window (Handle: {Handle})", windowHandle);

            var stateStr = _windowState.GetCurrentValue();
            var monitorIndex = _monitorIndex.GetCurrentValue();
            var monitorCount = PublicApi.GetMonitorCount();
            
            // Step 1: Move to target monitor (if needed)
            if (monitorIndex >= 0 && monitorIndex < monitorCount)
            {
                _logger.LogDebug("Moving window to monitor {MonitorIndex}", monitorIndex);
                PublicApi.MoveWindowToMonitor(windowHandle, monitorIndex);
                await Task.Delay(300, cancellationToken);
            }
            else if (monitorIndex >= monitorCount)
            {
                _logger.LogWarning("Monitor index {MonitorIndex} is out of range (0-{MaxIndex}). Skipping monitor move.",
                    monitorIndex, monitorCount - 1);
            }

            // Step 2: Apply window state or custom size (mutually exclusive)
            if (stateStr == "Maximized")
            {
                _logger.LogDebug("Maximizing window");
                PublicApi.SetWindowState(windowHandle, Shared.Platform.Windows.WindowState.Maximized);
                await Task.Delay(200, cancellationToken);
                if (PublicApi.GetWindowState(windowHandle) != Shared.Platform.Windows.WindowState.Maximized)
                {
                    _logger.LogDebug("Re-applying maximize state after revert");
                    PublicApi.SetWindowState(windowHandle, Shared.Platform.Windows.WindowState.Maximized);
                }
            }
            else if (stateStr == "Minimized")
            {
                _logger.LogDebug("Minimizing window");
                PublicApi.SetWindowState(windowHandle, Shared.Platform.Windows.WindowState.Minimized);
                await Task.Delay(100, cancellationToken);
                if (PublicApi.GetWindowState(windowHandle) != Shared.Platform.Windows.WindowState.Minimized)
                {
                    _logger.LogDebug("Re-applying minimize state after revert");
                    PublicApi.SetWindowState(windowHandle, Shared.Platform.Windows.WindowState.Minimized);
                }
            }
            else if (_useCustomSize.GetCurrentValue())
            {
                // Only for Normal state with custom size enabled
                var width = _windowWidth.GetCurrentValue();
                var height = _windowHeight.GetCurrentValue();
                _logger.LogDebug("Setting custom window size: {Width}x{Height}", width, height);
                PublicApi.SetWindowSize(windowHandle, width, height);
            }

            await Task.Delay(200, cancellationToken);

            // Step 3: Bring to foreground if requested (not for Minimized)
            if (_bringToForeground.GetCurrentValue() && stateStr != "Minimized")
            {
                _logger.LogDebug("Bringing window to foreground");
                PublicApi.FocusWindow(windowHandle);
            }

            _logger.LogInfo("Window configuration completed successfully for {ProcessName}",
                process.ProcessName);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Process window did not appear within timeout. Window configuration skipped.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during window configuration");
        }
    }

    /// <inheritdoc />
    public Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        if (_currentProcess == null || _currentProcess.HasExited)
        {
            _logger.LogDebug("Process already exited or not started");
            return Task.CompletedTask;
        }

        var lifecycle = _lifecycleMode.GetCurrentValue();
        _logger.LogInfo("Session ending. Lifecycle mode: {Mode}, Attached to existing: {Attached}",
            lifecycle, _attachedToExisting);

        if (lifecycle == "KeepRunning")
        {
            _logger.LogInfo("Keeping process {ProcessName} (PID: {ProcessId}) running",
                _currentProcess.ProcessName, _currentProcess.Id);
            return Task.CompletedTask;
        }

        // TerminateOnEnd mode
        if (_attachedToExisting)
        {
            _logger.LogWarning("Process was attached from existing. Closing main window only.");
            try
            {
                _currentProcess.CloseMainWindow();
                _logger.LogInfo("Main window closed for process {ProcessName}", _currentProcess.ProcessName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to close main window");
            }
        }
        else
        {
            _logger.LogInfo("Terminating process {ProcessName} (PID: {ProcessId})",
                _currentProcess.ProcessName, _currentProcess.Id);
            
            try
            {
                // Try graceful close first
                if (!_currentProcess.CloseMainWindow())
                {
                    _logger.LogDebug("CloseMainWindow failed, waiting 2 seconds before force kill");
                    if (!_currentProcess.WaitForExit(2000))
                    {
                        _logger.LogWarning("Process did not exit gracefully, forcing termination");
                        _currentProcess.Kill();
                    }
                }
                else
                {
                    _logger.LogDebug("Main window closed, waiting for process exit");
                    _currentProcess.WaitForExit(3000);
                }

                _logger.LogInfo("Process {ProcessName} terminated", _currentProcess.ProcessName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to terminate process");
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnActionAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CleanupAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Releases the resources used by the module, specifically the running process.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                var lifecycle = _lifecycleMode.GetCurrentValue();
                if (lifecycle == "TerminateOnEnd" && !_attachedToExisting)
                {
                    _logger.LogDebug("Disposing: killing process {ProcessName}", _currentProcess.ProcessName);
                    _currentProcess.Kill();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during process disposal");
        }
        finally
        {
            _currentProcess?.Dispose();
            _currentProcess = null;
        }

        GC.SuppressFinalize(this);
    }
}