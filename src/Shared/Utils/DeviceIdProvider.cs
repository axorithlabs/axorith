using System.Security.Cryptography;
using System.Text;

namespace Axorith.Shared.Utils;

/// <summary>
///     Provides a deterministic device ID based on machine-specific identifiers.
///     The device ID is stable across application restarts and does not require file caching.
/// </summary>
public static class DeviceIdProvider
{
    /// <summary>
    ///     Gets a deterministic device ID for the current machine.
    ///     The ID is generated from Environment.MachineName,
    ///     ensuring the same ID is always returned for the same machine combination.
    /// </summary>
    /// <returns>A GUID string representing the device ID.</returns>
    public static string GetDeviceId()
    {
        var machineIdentifier = $"{Environment.MachineName}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(machineIdentifier));

        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);

        return new Guid(guidBytes).ToString();
    }
}