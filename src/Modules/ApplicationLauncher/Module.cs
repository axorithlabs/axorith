#region

using System.Diagnostics;
using Axorith.Sdk;
using Axorith.Sdk.Settings;

#endregion

namespace Axorith.Module.ApplicationLauncher.Windows;

/// <summary>
/// A module that launches an external application at the start of a session.
/// </summary>
public class Module : IModule
{
    private Process? _currentProcess = null;

    /// <inheritdoc />
    public Guid Id => Guid.Parse("9b65a0b6-ce3e-4085-9ffa-b47c8fefcffd");

    /// <inheritdoc />
    public string Name => "Application Launcher Module";

    /// <inheritdoc />
    public string Description => "Launches a specified application when a session starts and optionally moves it to a specific monitor.";

    /// <inheritdoc />
    public string Category => "System";

    /// <inheritdoc />
    public IReadOnlySet<Platform> SupportedPlatforms => new HashSet<Platform> { Platform.Windows };

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
        if (!userSettings.TryGetValue("ApplicationPath", out var applicationPath) || string.IsNullOrWhiteSpace(applicationPath))
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
    public async Task OnSessionStartAsync(IModuleContext context, IReadOnlyDictionary<string, string> userSettings,
        CancellationToken cancellationToken)
    {
        context.LogInfo("Application Launcher Module is starting...");

        var applicationPath = userSettings.GetValueOrDefault("ApplicationPath");
        if (string.IsNullOrWhiteSpace(applicationPath))
        {
            context.LogError(null, "Application path is not specified. Module cannot start.");
            return;
        }
        
        var applicationArgs = userSettings.GetValueOrDefault("ApplicationArgs", string.Empty);
        
        if (!decimal.TryParse(userSettings.GetValueOrDefault("MonitorIndex", "0"), out var monitorIdx))
        {
            context.LogWarning("Could not parse 'MonitorIndex'. Using default value: 0.");
            monitorIdx = 0;
        }

        try
        {
            context.LogDebug("Attempting to start process: {Path} {Args}", applicationPath, applicationArgs);
            _currentProcess = Process.Start(applicationPath, applicationArgs);

            if (_currentProcess == null)
                throw new InvalidOperationException("Process.Start returned null.");
            
            context.LogInfo("Process {ProcessName} ({ProcessId}) started successfully.", _currentProcess.ProcessName, _currentProcess.Id);
        }
        catch (Exception ex)
        {
            context.LogError(ex, "Failed to start process for application: {ApplicationPath}. Check path and permissions.", applicationPath);
            return;
        }

        try
        {
            await Shared.Platform.Windows.WindowApi.WaitForWindowInitAsync(_currentProcess);

            if (_currentProcess.MainWindowHandle != IntPtr.Zero)
            {
                context.LogDebug("Main window handle found: {Handle}. Moving to monitor {MonitorIndex}", _currentProcess.MainWindowHandle, monitorIdx);
                Shared.Platform.Windows.WindowApi.MoveWindowToMonitor(_currentProcess.MainWindowHandle, (int)monitorIdx);
                context.LogInfo("Successfully moved window for process {ProcessName} to monitor {MonitorIndex}", _currentProcess.ProcessName, monitorIdx);
            }
            else
            {
                context.LogInfo("Process started without a graphical interface. Skipping window move.");
            }
        }
        catch (TimeoutException)
        {
            context.LogWarning("Process {ProcessName} started, but its main window did not appear in time. Could not move window.", _currentProcess.ProcessName);
        }
        catch (Exception ex)
        {
            context.LogError(ex, "An unexpected error occurred while trying to move the process window.");
        }

        context.LogInfo("Application Launcher Module has finished its work.");
    }

    /// <inheritdoc />
    public Task OnSessionEndAsync(IModuleContext context)
    {
        context.LogInfo("Application Launcher Module has been requested to shut down.");

        _currentProcess?.CloseMainWindow();

        return Task.CompletedTask;
    }
}