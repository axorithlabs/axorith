using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Axorith.Sdk.Services;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Unix;

/// <summary>
///     Unix-based (Linux/macOS) implementation of ISecureStorageService.
///     Uses AES encryption with machine-bound key derived from system identifiers.
///     
///     WARNING: This is a basic file-based encryption solution.
///     For production on Linux, consider integrating with libsecret/GNOME Keyring.
///     For production on macOS, consider integrating with Keychain Services.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal class UnixSecureStorage : ISecureStorageService
{
    private static readonly byte[] s_salt = Encoding.UTF8.GetBytes("AxorithLabs.Axorith.SecureStorage.v1");
    
    private readonly string _storagePath;
    private readonly ILogger _logger;
    private readonly UnixPlatform _platform;
    private readonly byte[] _encryptionKey;

    public UnixSecureStorage(ILogger logger, UnixPlatform platform)
    {
        _logger = logger;
        _platform = platform;
        
        // Determine storage path based on platform
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _storagePath = Path.Combine(homeDir, ".axorith", "secure_storage");
        Directory.CreateDirectory(_storagePath);

        // Set restrictive permissions (700 - owner only)
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try
            {
                File.SetUnixFileMode(_storagePath, 
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set directory permissions for secure storage");
            }
        }

        // Derive machine-bound encryption key
        _encryptionKey = DeriveEncryptionKey();
        
        _logger.LogInformation("{Platform} SecureStorage initialized at: {Path}", 
            platform == UnixPlatform.Linux ? "Linux" : "macOS", _storagePath);
        _logger.LogWarning(
            "Using file-based encryption. For production, consider integrating with " +
            (platform == UnixPlatform.Linux ? "libsecret/GNOME Keyring" : "macOS Keychain"));
    }

    public void StoreSecret(string key, string secret)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or whitespace", nameof(key));
        if (secret == null)
            throw new ArgumentNullException(nameof(secret));

        var filePath = GetFilePath(key);
        
        try
        {
            var plainData = Encoding.UTF8.GetBytes(secret);
            var encryptedData = Encrypt(plainData);
            
            File.WriteAllBytes(filePath, encryptedData);
            
            // Set file permissions (600 - owner read/write only)
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                try
                {
                    File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to set file permissions for secure storage file");
                }
            }
            
            _logger.LogDebug("Stored secure value for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store secure value for key: {Key}", key);
            throw;
        }
    }

    public string? RetrieveSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or whitespace", nameof(key));

        var filePath = GetFilePath(key);
        if (!File.Exists(filePath))
        {
            _logger.LogDebug("Secure value not found for key: {Key}", key);
            return null;
        }

        try
        {
            var encryptedData = File.ReadAllBytes(filePath);
            var plainData = Decrypt(encryptedData);
            return Encoding.UTF8.GetString(plainData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve secure value for key: {Key}", key);
            throw;
        }
    }

    public void DeleteSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or whitespace", nameof(key));

        var filePath = GetFilePath(key);
        
        if (!File.Exists(filePath))
        {
            _logger.LogDebug("Secure value not found for key (already deleted): {Key}", key);
            return;
        }

        try
        {
            // Overwrite file with random data before deletion (paranoid mode)
            var fileInfo = new FileInfo(filePath);
            var fileSize = fileInfo.Length;
            var randomData = new byte[fileSize];
            RandomNumberGenerator.Fill(randomData);
            File.WriteAllBytes(filePath, randomData);
            
            // Delete the file
            File.Delete(filePath);
            
            _logger.LogDebug("Deleted secure value for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete secure value for key: {Key}", key);
            throw;
        }
    }

    private byte[] Encrypt(byte[] plainData)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var cipherData = encryptor.TransformFinalBlock(plainData, 0, plainData.Length);

        // Prepend IV to ciphertext (IV is not secret)
        var result = new byte[aes.IV.Length + cipherData.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherData, 0, result, aes.IV.Length, cipherData.Length);

        return result;
    }

    private byte[] Decrypt(byte[] encryptedData)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;

        // Extract IV from beginning of encrypted data
        var iv = new byte[aes.IV.Length];
        Buffer.BlockCopy(encryptedData, 0, iv, 0, iv.Length);
        aes.IV = iv;

        var cipherData = new byte[encryptedData.Length - iv.Length];
        Buffer.BlockCopy(encryptedData, iv.Length, cipherData, 0, cipherData.Length);

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(cipherData, 0, cipherData.Length);
    }

    private byte[] DeriveEncryptionKey()
    {
        // Create machine-bound key from system identifiers
        // This makes the key specific to this machine + user
        var machineId = Environment.MachineName;
        var userName = Environment.UserName;
        var osVersion = Environment.OSVersion.ToString();
        
        var keyMaterial = $"{machineId}:{userName}:{osVersion}:AxorithLabs.Axorith.v1";
        var keyBytes = Encoding.UTF8.GetBytes(keyMaterial);
        
        // Use PBKDF2 to derive a proper AES key
        using var pbkdf2 = new Rfc2898DeriveBytes(keyBytes, s_salt, 10000, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32); // 256-bit key
    }

    private string GetFilePath(string key)
    {
        // Hash the key to create a safe filename
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_storagePath, keyHash);
    }
}
