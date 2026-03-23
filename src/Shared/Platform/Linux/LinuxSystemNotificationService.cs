namespace Axorith.Shared.Platform.Linux;

using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

[SupportedOSPlatform("linux")]
internal sealed class LinuxSystemNotificationService : ISystemNotificationService, IDisposable
{
    private readonly ILogger _logger;
    private readonly string _notificationService;
    private readonly bool _isAvailable;

    private const string FreedesktopService = "org.freedesktop.Notifications";
    private const string FreedesktopObjectPath = "/org/freedesktop/Notifications";

    public LinuxSystemNotificationService(ILogger logger)
    {
        _logger = logger;
        (_notificationService, _isAvailable) = DetectNotificationService();
    }

    private (string service, bool available) DetectNotificationService()
    {
        var services = new[] { "org.freedesktop.Notifications", "org.kde.StatusNotifierWatcher" };

        foreach (var service in services)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dbus-send",
                        Arguments = $"--session --dest={service} --type=method_call --print-reply {FreedesktopObjectPath} org.freedesktop.DBus.Ping",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                process.Start();
                var completed = process.WaitForExit(2000);

                if (completed && process.ExitCode == 0)
                {
                    _logger.LogDebug("Detected notification service: {Service}", service);
                    return (service, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to ping notification service {Service}", service);
            }
        }

        _logger.LogWarning("No D-Bus notification service available");
        return (string.Empty, false);
    }

    public async Task ShowNotificationAsync(string title, string message, TimeSpan? expiration = null)
    {
        if (!_isAvailable)
        {
            _logger.LogDebug("Notification skipped (service unavailable): {Title}", title);
            return;
        }

        var timeout = expiration?.TotalMilliseconds ?? 5000;

        try
        {
            var args = $"--session " +
                       $"--dest={_notificationService} " +
                       $"--type=method_call " +
                       $"--print-reply " +
                       $"{FreedesktopObjectPath} " +
                       $"org.freedesktop.Notifications.Notify " +
                       $"string:Axorith " +
                       $"uint32:0 " +
                       $"string: " +
                       $"string:\"{EscapeDbusString(title)}\" " +
                       $"string:\"{EscapeDbusString(message)}\" " +
                       $"array:string: " +
                       $"dict:string:variant: " +
                       $"int32:{(int)timeout}";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dbus-send",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                _logger.LogDebug("Notification sent: {Title}", title);
            }
            else
            {
                var error = await process.StandardError.ReadToEndAsync();
                _logger.LogWarning("Notification failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send notification: {Title}", title);
        }
    }

    private static string EscapeDbusString(string input)
    {
        return input
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    public void Dispose()
    {
    }
}
