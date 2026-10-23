using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Windows;

[SupportedOSPlatform("windows")]
internal class WindowsSystemNotificationService(ILogger logger) : ISystemNotificationService
{
    public async Task ShowNotificationAsync(string title, string message, TimeSpan? expiration = null)
    {
        try
        {
            var safeTitle = Convert.ToBase64String(Encoding.UTF8.GetBytes(title));
            var safeMessage = Convert.ToBase64String(Encoding.UTF8.GetBytes(message));
            var expirationSeconds = (expiration?.TotalSeconds ?? 10).ToString(CultureInfo.InvariantCulture);

            // PowerShell script using WinRT APIs directly
            var script = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null
$template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
$textNodes = $template.GetElementsByTagName('text')
$textNodes.Item(0).AppendChild($template.CreateTextNode([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{safeTitle}')))) > $null
$textNodes.Item(1).AppendChild($template.CreateTextNode([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{safeMessage}')))) > $null
$toast = [Windows.UI.Notifications.ToastNotification]::new($template)
$toast.ExpirationTime = [DateTimeOffset]::Now.AddSeconds({expirationSeconds})
$notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Axorith')
$notifier.Show($toast)
";

            // Encode script to Base64 (UTF-16LE) to prevent injection and encoding issues
            var scriptBytes = Encoding.Unicode.GetBytes(script);
            var encodedScript = Convert.ToBase64String(scriptBytes);

            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process();
            process.StartInfo = psi;
            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                logger.LogWarning("Failed to show notification via PowerShell. ExitCode: {Code}", process.ExitCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error showing system notification");
        }
    }
}
