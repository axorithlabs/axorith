using System.Diagnostics;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform.Windows;

namespace Axorith.Module.ApplicationLauncher.Windows;

/// <summary>
///     A module that launches an external application at the start of a session.
/// </summary>
public class Module : IModule
{
    private readonly IModuleLogger _logger;

    private readonly Setting<string> _applicationPath;
    private readonly Setting<string> _applicationArgs;
    private readonly Setting<int> _monitorIndex;

    private Process? _currentProcess;

    public Module(IModuleLogger logger)
    {
        _logger = logger;

        _applicationPath = Setting.AsFilePicker(
            key: "ApplicationPath",
            label: "Application Path",
            description: "The path to the application to launch.",
            defaultValue: @"C:\Windows\notepad.exe",
            filter: "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
        );

        _applicationArgs = Setting.AsText(
            key: "ApplicationArgs",
            label: "Application Args",
            description: "The arguments to pass to the application.",
            defaultValue: ""
        );

        _monitorIndex = Setting.AsInt(
            key: "MonitorIndex",
            label: "Target Monitor",
            description: "The index of the monitor to move the application window to.",
            defaultValue: 0
        );
    }

    /// <inheritdoc />
    public IReadOnlyList<ISetting> GetSettings()
    {
        return
        [
            _applicationPath,
            _applicationArgs,
            _monitorIndex
        ];
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return Array.Empty<IAction>();
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

        if (!File.Exists(_applicationPath.GetCurrentValue()))
            return Task.FromResult(ValidationResult.Fail($"File not found at '{_applicationPath.GetCurrentValue()}'."));

        if (_monitorIndex.GetCurrentValue() < 0)
            return Task.FromResult(ValidationResult.Fail("'Monitor Index' must be a non-negative number."));

        return Task.FromResult(ValidationResult.Success);
    }

    /// <inheritdoc />
    public async Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Attempting to start process: {Path} {Args}", _applicationPath.GetCurrentValue(),
                _applicationArgs.GetCurrentValue());
            _currentProcess = new Process();
            _currentProcess.StartInfo.FileName = _applicationPath.GetCurrentValue();
            _currentProcess.StartInfo.Arguments = _applicationArgs.GetCurrentValue();
            _currentProcess.StartInfo.UseShellExecute = true;
            _currentProcess.Start();

            _logger.LogInfo("Process {ProcessName} ({ProcessId}) started successfully.", _currentProcess.ProcessName,
                _currentProcess.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to start process for application: {ApplicationPath}. Check path and permissions.",
                _applicationPath.GetCurrentValue());
            return;
        }

        try
        {
            await WindowApi.WaitForWindowInitAsync(_currentProcess);

            if (_currentProcess.MainWindowHandle != IntPtr.Zero)
            {
                _logger.LogDebug("Main window handle found: {Handle}. Moving to monitor {MonitorIndex}",
                    _currentProcess.MainWindowHandle, _monitorIndex.GetCurrentValue());
                WindowApi.MoveWindowToMonitor(_currentProcess.MainWindowHandle, _monitorIndex.GetCurrentValue());
                _logger.LogInfo("Successfully moved window for process {ProcessName} to monitor {MonitorIndex}",
                    _currentProcess.ProcessName, _monitorIndex.GetCurrentValue());
            }
            else
            {
                _logger.LogInfo("Process started without a graphical interface. Skipping window move.");
            }
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Process {ProcessName} started, but its main window did not appear in time. Could not move window.",
                _currentProcess.ProcessName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while trying to move the process window.");
        }
    }

    /// <inheritdoc />
    public Task OnSessionEndAsync()
    {
        _currentProcess?.CloseMainWindow();

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Releases the resources used by the module, specifically the running process.
    /// </summary>
    public void Dispose()
    {
        if (_currentProcess is { HasExited: false }) _currentProcess.Kill();

        _currentProcess?.Dispose();
        _currentProcess = null;
        GC.SuppressFinalize(this);
    }
}