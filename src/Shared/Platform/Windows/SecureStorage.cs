using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Axorith.Sdk.Services;

namespace Axorith.Shared.Platform.Windows;

/// <summary>
///     A Windows-specific implementation of ISecureStorageService that uses DPAPI (ProtectedData)
///     to encrypt data based on the current user's credentials.
/// </summary>
[SupportedOSPlatform("windows")]
public class SecureStorage : ISecureStorageService
{
    // Using the application name as part of the entropy adds an extra layer of security.
    // It ensures that other apps on the system can't decrypt our data even if they run as the same user.
    private static readonly byte[] s_entropy = Encoding.UTF8.GetBytes("AxorithLabs.Axorith.v1");
    private readonly string _storagePath;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SecureStorage" /> class.
    ///     Ensures that the storage directory exists in the user's application data folder.
    /// </summary>
    public SecureStorage()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _storagePath = Path.Combine(appData, "Axorith", "secure_storage");
        Directory.CreateDirectory(_storagePath);
    }

    /// <inheritdoc />
    public void StoreSecret(string key, string secret)
    {
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var encryptedBytes = ProtectedData.Protect(secretBytes, s_entropy, DataProtectionScope.CurrentUser);
        var filePath = GetFilePathForKey(key);
        File.WriteAllBytes(filePath, encryptedBytes);
    }

    /// <inheritdoc />
    public string? RetrieveSecret(string key)
    {
        var filePath = GetFilePathForKey(key);
        if (!File.Exists(filePath)) return null;

        try
        {
            var encryptedBytes = File.ReadAllBytes(filePath);
            var secretBytes = ProtectedData.Unprotect(encryptedBytes, s_entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(secretBytes);
        }
        catch (CryptographicException)
        {
            // This can happen if the data is corrupt, from another user, or another machine.
            // We treat it as if the secret doesn't exist for security.
            return null;
        }
    }

    /// <summary>
    ///     Creates a safe filename by hashing the key. This avoids invalid characters
    ///     and prevents leaking key names into the filesystem.
    /// </summary>
    private string GetFilePathForKey(string key)
    {
        var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_storagePath, fileName);
    }
}