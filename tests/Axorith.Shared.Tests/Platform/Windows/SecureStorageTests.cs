using System.Security.Cryptography;
using System.Text;
using Axorith.Shared.Platform.Windows;
using FluentAssertions;

namespace Axorith.Shared.Tests.Platform.Windows;

public class SecureStorageTests
{
    [Fact]
    public void StoreRetrieveDelete_Roundtrip_OnWindows()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows-only

        try
        {
            // Arrange
            var storage = new SecureStorage();
            var key = $"test-key-{Guid.NewGuid()}";
            var secret = "super-secret-value";

            // Act
            storage.StoreSecret(key, secret);
            var retrieved = storage.RetrieveSecret(key);
            storage.DeleteSecret(key);
            var afterDelete = storage.RetrieveSecret(key);

            // Assert
            retrieved.Should().Be(secret);
            afterDelete.Should().BeNull();
        }
        catch (PlatformNotSupportedException)
        {
            // Skip on platforms where DPAPI is unavailable (e.g., CI agents running non-Windows).
        }
    }

    [Fact]
    public void RetrieveSecret_WithCorruptedData_ShouldReturnNull()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows-only

        try
        {
            // Arrange
            var storage = new SecureStorage();
            var key = $"corrupt-key-{Guid.NewGuid()}";
            storage.StoreSecret(key, "value");

            // Compute path to the stored file (same algorithm as in SecureStorage)
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var storageDir = Path.Combine(appData, "Axorith", "secure_storage");
            var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            var filePath = Path.Combine(storageDir, fileName);

            // Corrupt the file
            File.WriteAllBytes(filePath, new byte[] { 1, 2, 3, 4, 5 });

            // Act
            var result = storage.RetrieveSecret(key);

            // Cleanup
            storage.DeleteSecret(key);

            // Assert
            result.Should().BeNull();
        }
        catch (PlatformNotSupportedException)
        {
            // Skip on platforms where DPAPI is unavailable (e.g., CI agents running non-Windows).
        }
    }
}