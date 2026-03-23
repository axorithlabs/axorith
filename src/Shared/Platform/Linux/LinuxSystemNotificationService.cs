namespace Axorith.Shared.Platform.Linux;

using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Tmds.DBus;

[DBusInterface("org.freedesktop.Notifications")]
internal interface IFreedesktopNotifications
{
    Task<uint> NotifyAsync(
        string app_name,
        uint replaces_id,
        string app_icon,
        string summary,
        string body,
        string[] actions,
        IDictionary<string, object> hints,
        int expire_timeout);

    Task<string[]> GetCapabilitiesAsync();

    Task<(string name, string vendor, string version, string spec_version)> GetServerInformationAsync();

    Task CloseNotificationAsync(uint id);
}

[SupportedOSPlatform("linux")]
internal sealed class LinuxSystemNotificationService : ISystemNotificationService, IDisposable
{
    private readonly ILogger _logger;
    private readonly Connection? _connection;
    private readonly IFreedesktopNotifications? _notifications;
    private readonly bool _isAvailable;
    private bool _disposed;

    private const string NotificationsService = "org.freedesktop.Notifications";
    private const string NotificationsPath = "/org/freedesktop/Notifications";

    public LinuxSystemNotificationService(ILogger logger)
    {
        _logger = logger;
        (_connection, _notifications, _isAvailable) = ConnectAndCreateProxy();
    }

    private (Connection? connection, IFreedesktopNotifications? notifications, bool available) ConnectAndCreateProxy()
    {
        try
        {
            var connection = Connection.Session;
            connection.ConnectAsync().GetAwaiter().GetResult();
            _logger.LogDebug("Connected to D-Bus session bus at {Address}", Address.Session);

            IFreedesktopNotifications? notifications = null;
            var available = false;

            try
            {
                notifications = connection.CreateProxy<IFreedesktopNotifications>(
                    NotificationsService,
                    new ObjectPath(NotificationsPath));

                // Verify the service is reachable
                var info = notifications.GetServerInformationAsync().GetAwaiter().GetResult();
                _logger.LogInformation(
                    "D-Bus notification service available: {Name} v{Version} ({SpecVersion})",
                    info.name, info.version, info.spec_version);
                available = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "D-Bus notification service not reachable. " +
                    "Notifications will be unavailable on this session.");
            }

            return (connection, notifications, available);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to connect to D-Bus session bus. " +
                "Ensure the D-Bus session daemon is running (DBUS_SESSION_BUS_ADDRESS is set).");
            return (null, null, false);
        }
    }

    public async Task ShowNotificationAsync(string title, string message, TimeSpan? expiration = null)
    {
        if (!_isAvailable || _notifications == null || _disposed)
        {
            _logger.LogDebug("Notification skipped (service unavailable): {Title}", title);
            return;
        }

        var timeout = (int)(expiration?.TotalMilliseconds ?? 5000);

        try
        {
            var id = await _notifications.NotifyAsync(
                app_name: "Axorith",
                replaces_id: 0,
                app_icon: string.Empty,
                summary: title,
                body: message,
                actions: [],
                hints: new Dictionary<string, object>(),
                expire_timeout: timeout);

            _logger.LogDebug("Notification sent (id={Id}): {Title}", id, title);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send notification: {Title}", title);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _connection?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error disposing D-Bus connection");
        }
    }
}
