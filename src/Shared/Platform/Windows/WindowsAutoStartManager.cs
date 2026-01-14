using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Axorith.Shared.Platform.Windows;

/// <summary>
///     Windows implementation of auto-start management using registry.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutoStartManager : IAutoStartManager
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Axorith";
    private readonly ILogger _logger;
    private readonly string _executablePath;

    public WindowsAutoStartManager(ILogger logger)
    {
        _logger = logger;
        _executablePath = GetExecutablePath();
    }

    public bool IsAutoStartEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
                var value = key?.GetValue(AppName) as string;
                return !string.IsNullOrEmpty(value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check auto-start status");
                return false;
            }
        }
    }

    public bool IsStartMinimized
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
                var value = key?.GetValue(AppName) as string;
                return value?.Contains("--tray", StringComparison.OrdinalIgnoreCase) ?? false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check start minimized status");
                return false;
            }
        }
    }

    public bool EnableAutoStart(bool startMinimized = true)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
            if (key == null)
            {
                _logger.LogError("Failed to open registry key for auto-start");
                return false;
            }

            var command = startMinimized
                ? $"\"{_executablePath}\" --tray"
                : $"\"{_executablePath}\"";

            key.SetValue(AppName, command);
            _logger.LogInformation("Auto-start enabled (minimized: {Minimized})", startMinimized);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable auto-start");
            return false;
        }
    }

    public bool DisableAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
            if (key == null)
            {
                return true;
            }

            if (key.GetValue(AppName) != null)
            {
                key.DeleteValue(AppName, false);
                _logger.LogInformation("Auto-start disabled");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable auto-start");
            return false;
        }
    }

    private static string GetExecutablePath()
    {
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            return exePath;
        }

        var assembly = Assembly.GetEntryAssembly();
        return assembly?.Location ?? "Axorith.Client.exe";
    }
}