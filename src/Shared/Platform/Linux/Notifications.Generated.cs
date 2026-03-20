#nullable enable

namespace Axorith.Shared.Platform.Linux.Notifications.DBus;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

partial class NotificationsProxy
{
    public const string ServiceName = "org.freedesktop.Notifications";
    public const string ObjectPath = "/org/freedesktop/Notifications";
    public const string InterfaceName = "org.freedesktop.Notifications";

    private readonly DBusConnection _connection;

    public NotificationsProxy(DBusConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public Task<uint> NotifyAsync(
        string appName,
        uint replacesId,
        string appIcon,
        string summary,
        string body,
        string[] actions,
        Dictionary<string, VariantValue> hints,
        int expireTimeout)
    {
        return _connection.CallMethodAsync(
            CreateNotifyMessage(appName, replacesId, appIcon, summary, body, actions, hints, expireTimeout),
            ReadNotifyReply);
    }

    public Task<string[]> GetCapabilitiesAsync()
    {
        return _connection.CallMethodAsync(
            CreateGetCapabilitiesMessage(),
            ReadGetCapabilitiesReply);
    }

    private MessageBuffer CreateNotifyMessage(
        string appName,
        uint replacesId,
        string appIcon,
        string summary,
        string body,
        string[] actions,
        Dictionary<string, VariantValue> hints,
        int expireTimeout)
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: ServiceName,
            path: ObjectPath,
            @interface: InterfaceName,
            signature: "susssasa{sv}i",
            member: "Notify");
        writer.WriteString(appName);
        writer.WriteUInt32(replacesId);
        writer.WriteString(appIcon);
        writer.WriteString(summary);
        writer.WriteString(body);
        writer.WriteArray(actions);
        writer.WriteDictionary(hints);
        writer.WriteInt32(expireTimeout);
        return writer.CreateMessage();
    }

    private MessageBuffer CreateGetCapabilitiesMessage()
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: ServiceName,
            path: ObjectPath,
            @interface: InterfaceName,
            member: "GetCapabilities");
        return writer.CreateMessage();
    }

    private static uint ReadNotifyReply(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        return reader.ReadUInt32();
    }

    private static string[] ReadGetCapabilitiesReply(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        var arrayEnd = reader.ReadArrayStart(DBusType.String);
        var items = new List<string>();
        while (reader.HasNext(arrayEnd))
        {
            items.Add(reader.ReadString());
        }

        return items.ToArray();
    }
}
