using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Axorith.Core.Services;

public static class CommittedSessionStateFile
{
    private sealed record Envelope(string Payload, string Signature);
    private const string KeyFileName = "commitment-state.key";

    public static string ReadPayload(string path)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path))
                       ?? throw new InvalidDataException("Committed state envelope is empty.");
        var key = ReadKey(Path.GetDirectoryName(path)!);
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(envelope.Signature);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Committed state signature is invalid.", ex);
        }

        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(envelope.Payload));
        if (!CryptographicOperations.FixedTimeEquals(signature, expected))
        {
            throw new InvalidDataException("Committed state signature does not match.");
        }

        return envelope.Payload;
    }

    public static string ReadPayloadOrLegacy(string path)
    {
        var text = File.ReadAllText(path);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object &&
               (root.TryGetProperty(nameof(Envelope.Payload), out _) ||
                root.TryGetProperty(nameof(Envelope.Signature), out _))
            ? ReadPayload(path)
            : text;
    }

    public static void WritePayload(string path, string payload)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidDataException("Committed state path is invalid.");
        }

        Directory.CreateDirectory(directory);
        var key = ReadOrCreateKey(directory);
        var signature = Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));
        var tempPath = path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(new Envelope(payload, signature)));
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
    }

    private static byte[] ReadOrCreateKey(string directory)
    {
        var path = Path.Combine(directory, KeyFileName);
        if (!File.Exists(path))
        {
            var tempPath = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllBytes(tempPath, RandomNumberGenerator.GetBytes(32));
                File.Move(tempPath, path);
            }
            catch (IOException) when (File.Exists(path)) { }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        return ReadKey(directory);
    }

    private static byte[] ReadKey(string directory)
    {
        var key = File.ReadAllBytes(Path.Combine(directory, KeyFileName));
        return key.Length == 32
            ? key
            : throw new InvalidDataException("Committed state signing key is invalid.");
    }
}
