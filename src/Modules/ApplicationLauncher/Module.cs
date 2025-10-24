#region

using System.Diagnostics;
using Axorith.Sdk;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform.Windows;

#endregion

namespace Axorith.Module.ApplicationLauncher.Windows;

/// <summary>
///     A module that launches an external application at the start of a session.
/// </summary>
public class Module(IModuleLogger logger) : IModule
{
    private Process? _currentProcess;

    /// <inheritdoc />
    public IReadOnlyList<SettingBase> GetSettings()
    {
        return new List<SettingBase>
        {
            new TextSetting(
                "ApplicationPath",
                "Application Path",
                "The path to the application to launch.",
                @"C:\Windows\notepad.exe"
            ),
            new TextSetting(
                "ApplicationArgs",
                "Application Arguments",
                "The arguments to pass to the application.",
                @""
            ),
            new NumberSetting(
                "MonitorIndex",
                "Application Target Monitor",
                "The index of the monitor to move the application window to.",
                0
            )
        };
    }

    /// <inheritdoc />
    public Type? CustomSettingsViewType => null;

    /// <inheritdoc />
    public object? GetSettingsViewModel(IReadOnlyDictionary<string, string> currentSettings)
    {
        // This module uses auto-generated UI, so this method returns null.
        return null;
    }

    /// <inheritdoc />
    public Task<ValidationResult> ValidateSettingsAsync(IReadOnlyDictionary<string, string> userSettings,
        CancellationToken cancellationToken)
    {
        if (!userSettings.TryGetValue("ApplicationPath", out var applicationPath) ||
            string.IsNullOrWhiteSpace(applicationPath))
            return Task.FromResult(ValidationResult.Fail("'Application Path' is required."));

        if (!File.Exists(applicationPath))
            return Task.FromResult(ValidationResult.Fail($"File not found at '{applicationPath}'."));

        if (userSettings.TryGetValue("MonitorIndex", out var monitorIdxStr))
        {
            if (!decimal.TryParse(monitorIdxStr, out var monitorIdx))
                return Task.FromResult(ValidationResult.Fail("'Monitor Index' must be a valid number."));

            if (monitorIdx < 0)
                return Task.FromResult(ValidationResult.Fail("'Monitor Index' must be a non-negative number."));
        }

        return Task.FromResult(ValidationResult.Success);
    }

    /// <inheritdoc />
    public async Task OnSessionStartAsync(IReadOnlyDictionary<string, string> userSettings,
        CancellationToken cancellationToken)
    {
        var applicationPath = userSettings.GetValueOrDefault("ApplicationPath");
        if (string.IsNullOrWhiteSpace(applicationPath))
        {
            logger.LogError(null, "Application path is not specified. Module cannot start.");
            return;
        }

        var applicationArgs = userSettings.GetValueOrDefault("ApplicationArgs", string.Empty);

        if (!decimal.TryParse(userSettings.GetValueOrDefault("MonitorIndex", "0"), out var monitorIdx))
        {
            logger.LogWarning("Could not parse 'MonitorIndex'. Using default value: 0.");
            monitorIdx = 0;
        }

        try
        {
            logger.LogDebug("Attempting to start process: {Path} {Args}", applicationPath, applicationArgs);
            _currentProcess = new Process();
            _currentProcess.StartInfo.FileName = applicationPath;
            _currentProcess.StartInfo.Arguments = applicationArgs;
            _currentProcess.StartInfo.RedirectStandardOutput = true;
            _currentProcess.StartInfo.RedirectStandardError = true;
            _currentProcess.Start();

            logger.LogInfo("Process {ProcessName} ({ProcessId}) started successfully.", _currentProcess.ProcessName,
                _currentProcess.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to start process for application: {ApplicationPath}. Check path and permissions.",
                applicationPath);
            return;
        }

        try
        {
            await WindowApi.WaitForWindowInitAsync(_currentProcess);

            if (_currentProcess.MainWindowHandle != IntPtr.Zero)
            {
                logger.LogDebug("Main window handle found: {Handle}. Moving to monitor {MonitorIndex}",
                    _currentProcess.MainWindowHandle, monitorIdx);
                WindowApi.MoveWindowToMonitor(_currentProcess.MainWindowHandle, (int)monitorIdx);
                logger.LogInfo("Successfully moved window for process {ProcessName} to monitor {MonitorIndex}",
                    _currentProcess.ProcessName, monitorIdx);
            }
            else
            {
                logger.LogInfo("Process started without a graphical interface. Skipping window move.");
            }
        }
        catch (TimeoutException)
        {
            logger.LogWarning(
                "Process {ProcessName} started, but its main window did not appear in time. Could not move window.",
                _currentProcess.ProcessName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while trying to move the process window.");
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