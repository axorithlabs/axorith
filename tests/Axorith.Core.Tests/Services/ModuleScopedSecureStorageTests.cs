using Axorith.Core.Services;
using Axorith.Sdk;
using Axorith.Sdk.Services;
using FluentAssertions;
using Moq;

namespace Axorith.Core.Tests.Services;

/// <summary>
///     Tests for ModuleScopedSecureStorage that provides sandboxed secure storage per module
/// </summary>
public class ModuleScopedSecureStorageTests
{
    private readonly Mock<ISecureStorageService> _mockStorage;
    private readonly ModuleDefinition _moduleDefinition;
    private readonly ModuleScopedSecureStorage _scopedStorage;

    public ModuleScopedSecureStorageTests()
    {
        _mockStorage = new Mock<ISecureStorageService>();
        _moduleDefinition = new ModuleDefinition
        {
            Id = Guid.Parse("12345678-1234-1234-1234-123456789012"),
            Name = "Test Module"
        };

        _scopedStorage = new ModuleScopedSecureStorage(_mockStorage.Object, _moduleDefinition);
    }

    [Fact]
    public void StoreSecret_ShouldPrefixKeyWithModuleId()
    {
        // Arrange
        var key = "AccessToken";
        var secret = "secret-value";
        var expectedScopedKey = $"{_moduleDefinition.Id}:{key}";

        // Act
        _scopedStorage.StoreSecret(key, secret);

        // Assert
        _mockStorage.Verify(s => s.StoreSecret(expectedScopedKey, secret), Times.Once);
    }

    [Fact]
    public void RetrieveSecret_ShouldPrefixKeyWithModuleId()
    {
        // Arrange
        var key = "AccessToken";
        var expectedScopedKey = $"{_moduleDefinition.Id}:{key}";
        var expectedSecret = "secret-value";
        _mockStorage.Setup(s => s.RetrieveSecret(expectedScopedKey)).Returns(expectedSecret);

        // Act
        var result = _scopedStorage.RetrieveSecret(key);

        // Assert
        result.Should().Be(expectedSecret);
        _mockStorage.Verify(s => s.RetrieveSecret(expectedScopedKey), Times.Once);
    }

    [Fact]
    public void DeleteSecret_ShouldPrefixKeyWithModuleId()
    {
        // Arrange
        var key = "AccessToken";
        var expectedScopedKey = $"{_moduleDefinition.Id}:{key}";

        // Act
        _scopedStorage.DeleteSecret(key);

        // Assert
        _mockStorage.Verify(s => s.DeleteSecret(expectedScopedKey), Times.Once);
    }

    [Fact]
    public void MultipleKeys_ShouldAllBePrefixed()
    {
        // Arrange
        var key1 = "AccessToken";
        var key2 = "RefreshToken";
        var secret1 = "access-secret";
        var secret2 = "refresh-secret";

        // Act
        _scopedStorage.StoreSecret(key1, secret1);
        _scopedStorage.StoreSecret(key2, secret2);

        // Assert
        _mockStorage.Verify(s => s.StoreSecret($"{_moduleDefinition.Id}:{key1}", secret1), Times.Once);
        _mockStorage.Verify(s => s.StoreSecret($"{_moduleDefinition.Id}:{key2}", secret2), Times.Once);
    }

    [Fact]
    public void DifferentModules_ShouldHaveIsolatedKeys()
    {
        // Arrange
        var moduleDefinition2 = new ModuleDefinition
        {
            Id = Guid.Parse("87654321-4321-4321-4321-210987654321"),
            Name = "Another Module"
        };
        var scopedStorage2 = new ModuleScopedSecureStorage(_mockStorage.Object, moduleDefinition2);

        var key = "SharedKey";
        var secret1 = "module1-secret";
        var secret2 = "module2-secret";

        // Act
        _scopedStorage.StoreSecret(key, secret1);
        scopedStorage2.StoreSecret(key, secret2);

        // Assert
        _mockStorage.Verify(s => s.StoreSecret($"{_moduleDefinition.Id}:{key}", secret1), Times.Once);
        _mockStorage.Verify(s => s.StoreSecret($"{moduleDefinition2.Id}:{key}", secret2), Times.Once);
    }

    [Fact]
    public void RetrieveSecret_WhenNotFound_ShouldReturnNull()
    {
        // Arrange
        var key = "NonExistentKey";
        var expectedScopedKey = $"{_moduleDefinition.Id}:{key}";
        _mockStorage.Setup(s => s.RetrieveSecret(expectedScopedKey)).Returns((string?)null);

        // Act
        var result = _scopedStorage.RetrieveSecret(key);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void StoreAndRetrieve_RoundTrip_ShouldWork()
    {
        // Arrange
        var key = "TestKey";
        var secret = "test-secret-value";
        var scopedKey = $"{_moduleDefinition.Id}:{key}";

        _mockStorage.Setup(s => s.RetrieveSecret(scopedKey)).Returns(secret);

        // Act
        _scopedStorage.StoreSecret(key, secret);
        var retrieved = _scopedStorage.RetrieveSecret(key);

        // Assert
        retrieved.Should().Be(secret);
    }

    [Fact]
    public void StoreDelete_ShouldRemoveSecret()
    {
        // Arrange
        var key = "TestKey";
        var secret = "test-secret";
        var scopedKey = $"{_moduleDefinition.Id}:{key}";

        _mockStorage.Setup(s => s.RetrieveSecret(scopedKey)).Returns((string?)null);

        // Act
        _scopedStorage.StoreSecret(key, secret);
        _scopedStorage.DeleteSecret(key);
        var retrieved = _scopedStorage.RetrieveSecret(key);

        // Assert
        retrieved.Should().BeNull();
        _mockStorage.Verify(s => s.DeleteSecret(scopedKey), Times.Once);
    }

    [Fact]
    public void KeyScoping_ShouldPreventCrossModuleAccess()
    {
        // Arrange
        var key = "SharedSecret";
        var secret = "module1-only-secret";
        var scopedKey = $"{_moduleDefinition.Id}:{key}";

        _mockStorage.Setup(s => s.RetrieveSecret(scopedKey)).Returns(secret);
        _mockStorage.Setup(s => s.RetrieveSecret(It.Is<string>(k => k != scopedKey)))
            .Returns((string?)null);

        // Act
        _scopedStorage.StoreSecret(key, secret);
        var retrieved = _scopedStorage.RetrieveSecret(key);

        // Assert
        retrieved.Should().Be(secret);
        // Only the scoped key should be used
        _mockStorage.Verify(s => s.RetrieveSecret(scopedKey), Times.Once);
        _mockStorage.Verify(s => s.RetrieveSecret(key), Times.Never);
    }
}