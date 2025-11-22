using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Windows;

/// <summary>
///     Windows implementation of ISystemNotificationService using PowerShell.
///     This approach avoids complex WinRT/UWP dependencies in a headless .NET Core app
///     and works reliably without admin privileges on Windows 10/11.
/// </summary>
[SupportedOSPlatform("windows")]
internal class WindowsSystemNotificationService(ILogger logger) : ISystemNotificationService
{
    public async Task ShowNotificationAsync(string title, string message, TimeSpan? expiration = null)
    {
        try
        {
            // Escape single quotes for PowerShell string literal
            var safeTitle = title.Replace("'", "''");
            var safeMessage = message.Replace("'", "''");
            
            // PowerShell script to show a toast notification using the BurntToast module logic 
            // or direct .NET reflection to avoid external dependencies.
            // Here we use a direct .NET approach via PowerShell to keep it dependency-free.
            var script = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null
$template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
$textNodes = $template.GetElementsByTagName('text')
$textNodes.Item(0).AppendChild($template.CreateTextNode('{safeTitle}')) > $null
$textNodes.Item(1).AppendChild($template.CreateTextNode('{safeMessage}')) > $null
$toast = [Windows.UI.Notifications.ToastNotification]::new($template)
$toast.ExpirationTime = [DateTimeOffset]::Now.AddSeconds({(expiration?.TotalSeconds ?? 10)})
$notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Axorith')
$notifier.Show($toast)
";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command -",
                RedirectStandardInput = true,
                RedirectStandardOutput = false,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            await process.StandardInput.WriteAsync(script);
            process.StandardInput.Close();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                logger.LogWarning("Failed to show notification via PowerShell. ExitCode: {Code}. Error: {Error}", process.ExitCode, error);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error showing system notification");
        }
    }
}