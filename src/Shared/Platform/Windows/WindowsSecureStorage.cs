using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Axorith.Shared.Utils;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Windows;

[SupportedOSPlatform("windows")]
internal class WindowsSecureStorage : SecureStorageBase
{
    private static readonly byte[] LegacyEntropy = "AxorithLabs.Axorith.v1"u8.ToArray();
    private readonly string _storagePath;
    public WindowsSecureStorage(ILogger logger) : base(logger)
    {
        _storagePath = ApplicationPaths.EnsureDirectoryExists(ApplicationPaths.SecureStorage);
        Logger.LogDebug("Windows SecureStorage initialized at: {Path}", TelemetryGuard.SafePath(_storagePath));
    }

    protected override void StoreSecretCore(string key, string secret)
    {
        WriteSecretFile(GetFilePathForKey(key), ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null,
            DataProtectionScope.CurrentUser));
    }

    protected override string? RetrieveSecretCore(string key)
    {
        var filePath = GetFilePathForKey(key);
        if (!File.Exists(filePath))
            return null;

        try
        {
            var encryptedBytes = File.ReadAllBytes(filePath);
            var secretBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(secretBytes);
        }
        catch (CryptographicException)
        {
            try
            {
                var encryptedBytes = File.ReadAllBytes(filePath);
                var secretBytes = ProtectedData.Unprotect(encryptedBytes, LegacyEntropy,
                    DataProtectionScope.CurrentUser);
                var secret = Encoding.UTF8.GetString(secretBytes);
                StoreSecretCore(key, secret);
                Logger.LogInformation("Migrated legacy DPAPI secret for key: {Key}", key);
                return secret;
            }
            catch (CryptographicException legacyException)
            {
                Logger.LogWarning(legacyException,
                    "Failed to decrypt secret for key: {Key}. File: {FileName}, Size: {Size} bytes. " +
                    "Data may be corrupted or encrypted under a different user account.",
                    key, Path.GetFileName(filePath), new FileInfo(filePath).Length);
                return null;
            }
        }
    }

    protected override void DeleteSecretCore(string key)
    {
        var filePath = GetFilePathForKey(key);
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    private string GetFilePathForKey(string key)
    {
        var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_storagePath, fileName);
    }

    private static void WriteSecretFile(string filePath, byte[] encryptedBytes)
    {
        var tempFilePath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempFilePath, encryptedBytes);
            File.Move(tempFilePath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
        }
    }
}
