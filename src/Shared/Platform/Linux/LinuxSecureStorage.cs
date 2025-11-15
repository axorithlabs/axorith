using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Axorith.Sdk.Services;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Linux;

/// <summary>
///     Linux-specific secure storage implementation using Secret Service API (libsecret).
///     Falls back to encrypted file storage if Secret Service is not available.
/// </summary>
[SupportedOSPlatform("linux")]
internal class LinuxSecureStorage : ISecureStorageService
{
    private readonly ILogger _logger;
    private readonly string? _storageDir;
    private readonly bool _useSecretService;
    private const string SecretServiceLabel = "Axorith";

    public LinuxSecureStorage(ILogger logger)
    {
        _logger = logger;

        _useSecretService = IsSecretServiceAvailable();

        if (_useSecretService)
        {
            _logger.LogInformation("Using Linux Secret Service for secure storage");
        }
        else
        {
            _logger.LogWarning("Secret Service not available, falling back to encrypted file storage");
            _storageDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Axorith", "secrets"
            );
            Directory.CreateDirectory(_storageDir);

            // Set restrictive permissions (owner only)
            if (OperatingSystem.IsLinux())
                try
                {
                    // TODO: 
                    var dirInfo = new UnixDirectoryInfo(_storageDir)
                    {
                        FileAccessPermissions =
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to set directory permissions");
                }
        }
    }

    public void StoreSecret(string key, string secret)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or whitespace", nameof(key));
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Secret cannot be null or whitespace", nameof(secret));

        try
        {
            if (_useSecretService)
                StoreSecretViaSecretService(key, secret);
            else
                StoreSecretViaFile(key, secret);

            _logger.LogDebug("Stored secret for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing secret for key: {Key}", key);
            throw;
        }
    }

    public string? RetrieveSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or whitespace", nameof(key));

        try
        {
            var result = _useSecretService
                ? RetrieveSecretViaSecretService(key)
                : RetrieveSecretViaFile(key);

            if (result == null)
                _logger.LogDebug("No secret found for key: {Key}", key);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving secret for key: {Key}", key);
            throw;
        }
    }

    public void DeleteSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or whitespace", nameof(key));

        try
        {
            if (_useSecretService)
                DeleteSecretViaSecretService(key);
            else
                DeleteSecretViaFile(key);

            _logger.LogDebug("Deleted secret for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting secret for key: {Key}", key);
            throw;
        }
    }

    private static bool IsSecretServiceAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "secret-tool",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(1000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void StoreSecretViaSecretService(string key, string secret)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "secret-tool",
            Arguments = $"store --label=\"{SecretServiceLabel}\" application axorith key \"{key}\"",
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start secret-tool process");

        process.StandardInput.Write(secret);
        process.StandardInput.Close();
        process.WaitForExit(5000);

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"secret-tool failed: {error}");
        }
    }

    private static string? RetrieveSecretViaSecretService(string key)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "secret-tool",
            Arguments = $"lookup application axorith key \"{key}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start secret-tool process");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);

        switch (process.ExitCode)
        {
            // Exit code 1 means not found
            case 1:
                return null;
            case 0:
                return string.IsNullOrWhiteSpace(output) ? null : output.TrimEnd('\n');
            default:
            {
                var error = process.StandardError.ReadToEnd();
                throw new InvalidOperationException($"secret-tool failed: {error}");
            }
        }
    }

    private static void DeleteSecretViaSecretService(string key)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "secret-tool",
            Arguments = $"clear application axorith key \"{key}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start secret-tool process");

        process.WaitForExit(5000);

        // Exit code 1 means not found (which is OK for delete)
        if (process.ExitCode is 0 or 1) return;

        var error = process.StandardError.ReadToEnd();
        throw new InvalidOperationException($"secret-tool failed: {error}");
    }

    private void StoreSecretViaFile(string key, string secret)
    {
        if (_storageDir == null)
            throw new InvalidOperationException("Storage directory not initialized");

        var fileName = GetSecretFileName(key);
        var filePath = Path.Combine(_storageDir, fileName);

        // Simple XOR encryption with machine-specific key
        var encryptionKey = GetMachineKey();
        var encryptedData = XorEncrypt(Encoding.UTF8.GetBytes(secret), encryptionKey);

        File.WriteAllBytes(filePath, encryptedData);

        // Set restrictive file permissions
        if (OperatingSystem.IsLinux())
            try
            {
                // TODO
                var fileInfo = new UnixFileInfo(filePath)
                {
                    FileAccessPermissions = UnixFileMode.UserRead | UnixFileMode.UserWrite
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set file permissions for {File}", filePath);
            }
    }

    private string? RetrieveSecretViaFile(string key)
    {
        if (_storageDir == null)
            throw new InvalidOperationException("Storage directory not initialized");

        var fileName = GetSecretFileName(key);
        var filePath = Path.Combine(_storageDir, fileName);

        if (!File.Exists(filePath))
            return null;

        var encryptedData = File.ReadAllBytes(filePath);
        var encryptionKey = GetMachineKey();
        var decryptedData = XorEncrypt(encryptedData, encryptionKey);

        return Encoding.UTF8.GetString(decryptedData);
    }

    private void DeleteSecretViaFile(string key)
    {
        if (_storageDir == null)
            throw new InvalidOperationException("Storage directory not initialized");

        var fileName = GetSecretFileName(key);
        var filePath = Path.Combine(_storageDir, fileName);

        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    private static string GetSecretFileName(string key)
    {
        // Use SHA256 hash of key as filename for security
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hashBytes).ToLowerInvariant() + ".dat";
    }

    private static byte[] GetMachineKey()
    {
        // Create machine-specific encryption key
        var machineId = Environment.MachineName + Environment.UserName;
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(machineId));
    }

    private static byte[] XorEncrypt(byte[] data, byte[] key)
    {
        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++) result[i] = (byte)(data[i] ^ key[i % key.Length]);
        return result;
    }
}

/// <summary>
///     Helper class for Unix file permissions
/// </summary>
file class UnixFileInfo(string path)
{
    private readonly FileInfo _fileInfo = new(path);

    public UnixFileMode FileAccessPermissions
    {
        set
        {
            var octal = Convert.ToString((int)value, 8);
            var psi = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"{octal} \"{_fileInfo.FullName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(1000);
        }
    }
}

/// <summary>
///     Helper class for Unix directory permissions
/// </summary>
file class UnixDirectoryInfo(string path)
{
    private readonly DirectoryInfo _dirInfo = new(path);

    public UnixFileMode FileAccessPermissions
    {
        set
        {
            var octal = Convert.ToString((int)value, 8);
            var psi = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"{octal} \"{_dirInfo.FullName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(1000);
        }
    }
}