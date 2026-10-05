using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Axorith.Sdk.Services;
using Axorith.Shared.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Axorith.Shared.Tests.Platform;

public sealed class SecureStorageMigrationTests
{
    [WindowsDpapiFact]
    [SupportedOSPlatform("windows")]
    public void LegacyDpapiSecretIsReadAndRewrittenWithCurrentFormat()
    {
        const string key = "legacy-secret";
        const string secret = "migration-value";
        var directory = Directory.CreateTempSubdirectory("axorith-dpapi-migration-");
        var previousRoot = Environment.GetEnvironmentVariable("AXORITH_ROAMING_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("AXORITH_ROAMING_ROOT", directory.FullName);
            var storageDirectory = Path.Combine(directory.FullName, "secure_storage");
            Directory.CreateDirectory(storageDirectory);
            var secretPath = Path.Combine(storageDirectory,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))));
            File.WriteAllBytes(secretPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(secret),
                "AxorithLabs.Axorith.v1"u8.ToArray(), DataProtectionScope.CurrentUser));

            ISecureStorageService storage = PlatformServices.CreateSecureStorage(NullLogger.Instance);

            Assert.Equal(secret, storage.RetrieveSecret(key));
            var rewritten = File.ReadAllBytes(secretPath);
            Assert.Equal(secret, Encoding.UTF8.GetString(
                ProtectedData.Unprotect(rewritten, null, DataProtectionScope.CurrentUser)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AXORITH_ROAMING_ROOT", previousRoot);
            directory.Delete(recursive: true);
        }
    }

    [LinuxFileStorageFact]
    [SupportedOSPlatform("linux")]
    public void LegacyPlaintextMasterKeyKeepsExistingSecretsReadable()
    {
        const string key = "legacy-secret";
        const string secret = "migration-value";
        var directory = Directory.CreateTempSubdirectory("axorith-linux-key-migration-");
        var previousRoot = Environment.GetEnvironmentVariable("AXORITH_LOCAL_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("AXORITH_LOCAL_ROOT", directory.FullName);
            var storageDirectory = Path.Combine(directory.FullName, "secrets");
            Directory.CreateDirectory(storageDirectory);
            var masterKey = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(Path.Combine(storageDirectory, "master.key"), masterKey);

            var nonce = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[Encoding.UTF8.GetByteCount(secret)];
            var tag = new byte[16];
            using (var aes = new AesGcm(masterKey, tag.Length))
                aes.Encrypt(nonce, Encoding.UTF8.GetBytes(secret), ciphertext, tag);
            var secretFile = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant() +
                             ".dat";
            File.WriteAllBytes(Path.Combine(storageDirectory, secretFile), [.. nonce, .. ciphertext, .. tag]);

            ISecureStorageService storage = PlatformServices.CreateSecureStorage(NullLogger.Instance);

            Assert.Equal(secret, storage.RetrieveSecret(key));
            Assert.NotEqual(32, new FileInfo(Path.Combine(storageDirectory, "master.key")).Length);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AXORITH_LOCAL_ROOT", previousRoot);
            directory.Delete(recursive: true);
        }
    }

    private sealed class WindowsDpapiFactAttribute : FactAttribute
    {
        public WindowsDpapiFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
            {
                Skip = "DPAPI migration requires Windows.";
                return;
            }

            try
            {
                ProtectedData.Protect([1], null, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException)
            {
                Skip = "The current Windows user profile does not provide DPAPI.";
            }
        }
    }

    private sealed class LinuxFileStorageFactAttribute : FactAttribute
    {
        public LinuxFileStorageFactAttribute()
        {
            if (!OperatingSystem.IsLinux())
                Skip = "File-backed secure-storage migration requires Linux.";
            else if (HasSecretTool())
                Skip = "Linux Secret Service is installed and takes precedence over file storage.";
        }

        private static bool HasSecretTool() => (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, "secret-tool")));
    }
}
