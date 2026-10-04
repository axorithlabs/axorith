using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Axorith.Shared.Utils;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Linux;

[SupportedOSPlatform("linux")]
internal class LinuxSecureStorage : SecureStorageBase
{
    private readonly string? _storageDir;
    private readonly bool _useSecretService;
    private const string SecretServiceLabel = "Axorith";
    private const string FileKeyName = "master.key";

    public LinuxSecureStorage(ILogger logger) : base(logger)
    {
        _useSecretService = IsSecretServiceAvailable();

        if (_useSecretService)
        {
            Logger.LogInformation("Using Linux Secret Service for secure storage");
        }
        else
        {
            Logger.LogWarning("Secret Service not available, falling back to encrypted file storage");
            _storageDir = ApplicationPaths.EnsureDirectoryExists(ApplicationPaths.LocalSecrets);

            // Set restrictive permissions (owner only)
            if (OperatingSystem.IsLinux())
            {
                try
                {
                    File.SetUnixFileMode(_storageDir,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to set directory permissions");
                }
            }
        }
    }

    protected override void ValidateSecret(string secret) => ArgumentException.ThrowIfNullOrWhiteSpace(secret);

    protected override void StoreSecretCore(string key, string secret)
    {
        if (_useSecretService)
            StoreSecretViaSecretService(key, secret);
        else
            StoreSecretViaFile(key, secret);
    }

    protected override string? RetrieveSecretCore(string key) => _useSecretService
        ? RetrieveSecretViaSecretService(key)
        : RetrieveSecretViaFile(key);

    protected override void DeleteSecretCore(string key)
    {
        if (_useSecretService)
            DeleteSecretViaSecretService(key);
        else
            DeleteSecretViaFile(key);
    }

    private static bool IsSecretServiceAvailable()
    {
        try
        {
            return CommandRunner.Run("which", ["secret-tool"], timeoutMs: 1000).ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void StoreSecretViaSecretService(string key, string secret) =>
        CommandRunner.RunChecked("secret-tool",
            ["store", $"--label={SecretServiceLabel}", "application", "axorith", "key", key], secret);

    private static string? RetrieveSecretViaSecretService(string key)
    {
        var result = CommandRunner.Run("secret-tool", ["lookup", "application", "axorith", "key", key]);
        return result.ExitCode switch
        {
            0 => string.IsNullOrWhiteSpace(result.Output) ? null : result.Output.TrimEnd('\r', '\n'),
            1 => null,
            _ => throw new InvalidOperationException($"secret-tool failed: {result.Error}")
        };
    }

    private static void DeleteSecretViaSecretService(string key)
    {
        var result = CommandRunner.Run("secret-tool", ["clear", "application", "axorith", "key", key]);
        if (result.ExitCode is not (0 or 1))
            throw new InvalidOperationException($"secret-tool failed: {result.Error}");
    }

    private void StoreSecretViaFile(string key, string secret)
    {
        if (_storageDir == null)
        {
            throw new InvalidOperationException("Storage directory not initialized");
        }

        var fileName = GetSecretFileName(key);
        var filePath = Path.Combine(_storageDir, fileName);

        var encryptionKey = GetOrCreateFileEncryptionKey();
        var plaintextBytes = Encoding.UTF8.GetBytes(secret);
        var encryptedData = EncryptAesGcm(plaintextBytes, encryptionKey);

        File.WriteAllBytes(filePath, encryptedData);

        // Set restrictive file permissions
        if (OperatingSystem.IsLinux())
        {
            try
            {
                File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to set file permissions for {File}", TelemetryGuard.SafePath(filePath));
            }
        }
    }

    private string? RetrieveSecretViaFile(string key)
    {
        if (_storageDir == null)
        {
            throw new InvalidOperationException("Storage directory not initialized");
        }

        var fileName = GetSecretFileName(key);
        var filePath = Path.Combine(_storageDir, fileName);

        if (!File.Exists(filePath))
        {
            return null;
        }

        var encryptedData = File.ReadAllBytes(filePath);
        var encryptionKey = GetOrCreateFileEncryptionKey();

        try
        {
            var decryptedData = DecryptAesGcm(encryptedData, encryptionKey, "Ciphertext too short");
            return Encoding.UTF8.GetString(decryptedData);
        }
        catch (CryptographicException)
        {
            // Backward compatibility: try legacy XOR-based fallback for already stored secrets
            try
            {
                var legacyKey = GetMachineKey();
                var decryptedLegacy = XorEncrypt(encryptedData, legacyKey);
                return Encoding.UTF8.GetString(decryptedLegacy);
            }
            catch
            {
                return null;
            }
        }
    }

    private void DeleteSecretViaFile(string key)
    {
        if (_storageDir == null)
        {
            throw new InvalidOperationException("Storage directory not initialized");
        }

        var fileName = GetSecretFileName(key);
        var filePath = Path.Combine(_storageDir, fileName);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private static string GetSecretFileName(string key)
    {
        // Use SHA256 hash of key as filename for security
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hashBytes).ToLowerInvariant() + ".dat";
    }

    private byte[] GetOrCreateFileEncryptionKey()
    {
        if (_storageDir == null)
        {
            throw new InvalidOperationException("Storage directory not initialized");
        }

        var keyPath = Path.Combine(_storageDir, FileKeyName);

        if (File.Exists(keyPath))
        {
            try
            {
                var storedEncryptedKey = File.ReadAllBytes(keyPath);
                var derivedMachineKey = GetMachineKey();
                return DecryptAesGcm(storedEncryptedKey, derivedMachineKey, "Encrypted key too short");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "Failed to decrypt master key. Regenerating new key. Previous secrets will be inaccessible.");
                File.Delete(keyPath);
            }
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var machineKey = GetMachineKey();
        var encryptedKey = EncryptAesGcm(key, machineKey);

        File.WriteAllBytes(keyPath, encryptedKey);

        if (OperatingSystem.IsLinux())
        {
            try
            {
                File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to set file permissions for key file {File}",
                    TelemetryGuard.SafePath(keyPath));
            }
        }

        return key;
    }

    private static byte[] GetMachineKey()
    {
        var components = new List<string>
        {
            Environment.MachineName,
            Environment.UserName,
            Environment.UserDomainName
        };

        if (OperatingSystem.IsLinux())
        {
            try
            {
                var machineIdFile = "/etc/machine-id";
                if (File.Exists(machineIdFile))
                {
                    components.Add(File.ReadAllText(machineIdFile).Trim());
                }

                var dbusIdFile = "/var/lib/dbus/machine-id";
                if (File.Exists(dbusIdFile))
                {
                    components.Add(File.ReadAllText(dbusIdFile).Trim());
                }

                var productUuidFile = "/sys/class/dmi/id/product_uuid";
                if (File.Exists(productUuidFile))
                {
                    try
                    {
                        components.Add(File.ReadAllText(productUuidFile).Trim());
                    }
                    catch
                    {
                        // May require root, skip if not accessible
                    }
                }
            }
            catch
            {
                // Fallback to basic machine ID
            }
        }

        var combinedData = string.Join("|", components);

        using var sha512 = SHA512.Create();
        var hash = sha512.ComputeHash(Encoding.UTF8.GetBytes(combinedData));

        var key = new byte[32];
        Array.Copy(hash, key, 32);
        return key;
    }

    private static byte[] EncryptAesGcm(byte[] data, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[data.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, data, ciphertext, tag);

        return [.. nonce, .. ciphertext, .. tag];
    }

    private static byte[] DecryptAesGcm(byte[] data, byte[] key, string invalidDataMessage)
    {
        const int nonceLength = 12;
        const int tagLength = 16;
        if (data.Length < nonceLength + tagLength)
        {
            throw new CryptographicException(invalidDataMessage);
        }

        var ciphertextLength = data.Length - nonceLength - tagLength;
        var plaintext = new byte[ciphertextLength];
        using var aes = new AesGcm(key, tagLength);
        aes.Decrypt(
            data.AsSpan(0, nonceLength),
            data.AsSpan(nonceLength, ciphertextLength),
            data.AsSpan(nonceLength + ciphertextLength, tagLength),
            plaintext);
        return plaintext;
    }

    private static byte[] XorEncrypt(byte[] data, byte[] key)
    {
        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++) result[i] = (byte)(data[i] ^ key[i % key.Length]);
        return result;
    }
}
