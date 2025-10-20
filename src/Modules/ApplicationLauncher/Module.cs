#region

using System.Diagnostics;
using Axorith.Sdk;
using Axorith.Sdk.Settings;

#endregion

namespace Axorith.Module.ApplicationLauncher.Windows;

public class Module : IModule
{
    private Process? _currentProcess = null;

    /// <inheritdoc />
    public Guid Id => Guid.Parse("9b65a0b6-ce3e-4085-9ffa-b47c8fefcffd");

    /// <inheritdoc />
    public string Name => "Application Launcher Module";

    /// <inheritdoc />
    public string Description => "";

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
                "",
                @"C:\Windows\notepad.exe"
            ),
            new TextSetting(
                "ApplicationArgs",
                "Application Args",
                "",
                @""
            ),
            new NumberSetting(
                "MonitorIndex",
                "Monitor Index",
                "",
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
        if (!userSettings.TryGetValue("ApplicationPath", out var applicationPath))
            return Task.FromResult(ValidationResult.Success);

        if (!userSettings.TryGetValue("ApplicationArgs", out var applicationArgs))
            return Task.FromResult(ValidationResult.Success);

        if (!userSettings.TryGetValue("MonitorIndex", out var monitorIdxStr))
            return Task.FromResult(ValidationResult.Success);

        if (!File.Exists(applicationPath))
            return Task.FromResult(ValidationResult.Fail("'Application Path' must be a valid path to application."));

        if (!decimal.TryParse(monitorIdxStr, out var monitorIdx) && monitorIdx < 0)
            return Task.FromResult(ValidationResult.Fail("'Monitor Index' must be a non-negative number."));

        return Task.FromResult(ValidationResult.Success);
    }

    /// <inheritdoc />
    public async Task OnSessionStartAsync(IModuleContext context, IReadOnlyDictionary<string, string> userSettings,
        CancellationToken cancellationToken)
    {
        context.LogInfo("Application Launcher Module is starting...");

        var applicationPath = userSettings.GetValueOrDefault("ApplicationPath", @"C:\Windows\notepad.exe");
        var applicationArgs = userSettings.GetValueOrDefault("ApplicationArgs", string.Empty);

        if (!decimal.TryParse(userSettings.GetValueOrDefault("MonitorIndex", "0"), out var monitorIdx))
        {
            monitorIdx = 0;
            context.LogWarning("Could not parse 'MonitorIndex'. Using default value: {MonitorIdx}", monitorIdx);
        }

        _currentProcess = Process.Start(applicationPath, applicationArgs);
        
        try
        {
            _currentProcess.WaitForInputIdle();
            await Shared.Platform.Windows.WindowApi.WaitForWindowInitAsync(_currentProcess);
            Shared.Platform.Windows.WindowApi.MoveWindowToMonitor(_currentProcess.MainWindowHandle, (int)monitorIdx);
        }
        catch (Exception e)
        {
            context.LogError(e, "Failed to move process window to monitor");
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