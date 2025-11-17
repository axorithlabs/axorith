using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Axorith.Sdk.Services;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Windows;

/// <summary>
///     Windows-specific implementation of ISecureStorageService using DPAPI (ProtectedData).
///     Encrypts data based on the current user's Windows credentials.
/// </summary>
[SupportedOSPlatform("windows")]
internal class WindowsSecureStorage : ISecureStorageService
{
    // Application-specific entropy for additional security layer
    // Prevents other apps from decrypting our data even under same user
    private static readonly byte[] SEntropy = "AxorithLabs.Axorith.v1"u8.ToArray();

    private readonly string _storagePath;
    private readonly ILogger _logger;

    public WindowsSecureStorage(ILogger logger)
    {
        _logger = logger;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _storagePath = Path.Combine(appData, "Axorith", "secure_storage");
        Directory.CreateDirectory(_storagePath);

        _logger.LogDebug("Windows SecureStorage initialized at: {Path}", _storagePath);
    }

    public void StoreSecret(string key, string secret)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or whitespace", nameof(key));
        if (secret == null)
            throw new ArgumentNullException(nameof(secret));

        try
        {
            var secretBytes = Encoding.UTF8.GetBytes(secret);
            var encryptedBytes = ProtectedData.Protect(secretBytes, SEntropy, DataProtectionScope.CurrentUser);
            var filePath = GetFilePathForKey(key);

            File.WriteAllBytes(filePath, encryptedBytes);

            _logger.LogTrace("Stored secret for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store secret for key: {Key}", key);
            throw;
        }
    }

    public string? RetrieveSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or whitespace", nameof(key));

        var filePath = GetFilePathForKey(key);
        if (!File.Exists(filePath))
        {
            _logger.LogTrace("Secret not found for key: {Key}", key);
            return null;
        }

        try
        {
            var encryptedBytes = File.ReadAllBytes(filePath);
            var secretBytes = ProtectedData.Unprotect(encryptedBytes, SEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(secretBytes);
        }
        catch (CryptographicException ex)
        {
            // Graceful degradation: data corrupted or encrypted under different user
            _logger.LogWarning(ex,
                "Failed to decrypt secret for key: {Key}. File: {FileName}, Size: {Size} bytes. " +
                "Data may be corrupted or encrypted under a different user account.",
                key, Path.GetFileName(filePath), new FileInfo(filePath).Length);
            return null;
        }
    }

    public void DeleteSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or whitespace", nameof(key));

        var filePath = GetFilePathForKey(key);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogTrace("Deleted secret for key: {Key}", key);
        }
    }

    /// <summary>
    ///     Creates a safe filename by hashing the key with SHA256.
    ///     Prevents invalid characters and information leakage through filesystem.
    /// </summary>
    private string GetFilePathForKey(string key)
    {
        var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_storagePath, fileName);
    }
}