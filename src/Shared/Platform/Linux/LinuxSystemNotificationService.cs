namespace Axorith.Shared.Platform.Linux;

using System.Runtime.Versioning;
using Axorith.Shared.Platform.Linux.Notifications.DBus;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

/// <summary>
///     Linux implementation of ISystemNotificationService using D-Bus org.freedesktop.Notifications.
///     Works on GNOME, KDE, XFCE, and other freedesktop-compliant desktops.
///     Gracefully degrades when the notification daemon is unavailable.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxSystemNotificationService : ISystemNotificationService, IDisposable
{
    private readonly ILogger _logger;
    private readonly DBusConnection? _connection;
    private readonly NotificationsProxy? _proxy;
    private readonly bool _isAvailable;
    private bool _disposed;

    public LinuxSystemNotificationService(ILogger logger)
    {
        _logger = logger;

        DBusConnection? connection = null;
        try
        {
            string? sessionBusAddress = DBusAddress.Session;
            if (sessionBusAddress is null)
            {
                _logger.LogDebug("No D-Bus session bus address found");
                _isAvailable = false;
                return;
            }

            connection = new DBusConnection(sessionBusAddress);
            connection.ConnectAsync().GetAwaiter().GetResult();

            // Probe: try to call GetCapabilities to verify service is reachable
            var proxy = new NotificationsProxy(connection);
            _ = proxy.GetCapabilitiesAsync().GetAwaiter().GetResult();

            _connection = connection;
            _proxy = proxy;
            _isAvailable = true;
            _logger.LogInformation("D-Bus notification service available");
        }
        catch (DBusConnectionException ex)
        {
            _logger.LogWarning(ex, "D-Bus connection failed: notification service unavailable");
            _isAvailable = false;
            connection?.Dispose();
        }
        catch (DBusErrorReplyException ex)
        {
            _logger.LogWarning(ex, "D-Bus notification service unavailable");
            _isAvailable = false;
            connection?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error probing D-Bus notification service");
            _isAvailable = false;
            connection?.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task ShowNotificationAsync(
        string title,
        string message,
        TimeSpan? expiration = null)
    {
        if (_disposed)
        {
            return;
        }

        if (!_isAvailable || _proxy is null)
        {
            _logger.LogDebug("Notification skipped: service unavailable");
            return;
        }

        try
        {
            var expireTimeout = expiration.HasValue
                ? (int)expiration.Value.TotalMilliseconds
                : -1; // -1 = use server default

            await _proxy.NotifyAsync(
                appName: "Axorith",
                replacesId: 0,
                appIcon: string.Empty,
                summary: title,
                body: message,
                actions: [],
                hints: [],
                expireTimeout: expireTimeout);

            _logger.LogDebug("Notification sent: {Title}", title);
        }
        catch (DBusConnectionException ex)
        {
            _logger.LogWarning(ex, "Failed to send notification: connection lost");
        }
        catch (DBusErrorReplyException ex)
        {
            _logger.LogWarning(ex, "Failed to send notification: {Error}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error sending notification");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection?.Dispose();
    }
}
