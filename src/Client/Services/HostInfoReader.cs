using System.Text.Json;

namespace Axorith.Client.Services;

internal static class HostInfoReader
{
    public static bool TryReadPort(string path, out int port)
    {
        port = 0;
        try
        {
            return File.Exists(path) && TryParsePort(File.ReadAllText(path), out port);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool TryParsePort(string json, out int port)
    {
        port = 0;
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("port", out var value) &&
                   value.TryGetInt32(out port) && port is > 0 and <= 65535;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
