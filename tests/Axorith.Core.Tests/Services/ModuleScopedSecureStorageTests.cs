using Axorith.Core.Services;
using Axorith.Sdk;
using Axorith.Sdk.Services;
using FluentAssertions;

namespace Axorith.Core.Tests.Services;

public sealed class ModuleScopedSecureStorageTests
{
    private readonly MemoryStorage _storage = new();
    private readonly Guid _moduleId = Guid.NewGuid();
    private ModuleScopedSecureStorage Scope(Guid id) => new(_storage, new ModuleDefinition { Id = id, Name = "Test" });

    [Fact]
    public void StoredSecretsCanBeReadThroughANewScopeAndUpdatedIndependently()
    {
        Scope(_moduleId).StoreSecret("AccessToken", "first");
        Scope(_moduleId).StoreSecret("RefreshToken", "refresh");
        Scope(_moduleId).RetrieveSecret("AccessToken").Should().Be("first");
        Scope(_moduleId).StoreSecret("AccessToken", "updated");

        Scope(_moduleId).RetrieveSecret("AccessToken").Should().Be("updated");
        Scope(_moduleId).RetrieveSecret("RefreshToken").Should().Be("refresh");
        _storage.RetrieveSecret($"{_moduleId}:AccessToken").Should().Be("updated");
        _storage.RetrieveSecret("AccessToken").Should().BeNull();
    }

    [Fact]
    public void ModulesCannotReadOverwriteOrDeleteEachOthersSecrets()
    {
        var otherId = Guid.NewGuid();
        Scope(_moduleId).StoreSecret("SharedKey", "first-module");
        Scope(otherId).RetrieveSecret("SharedKey").Should().BeNull();
        Scope(otherId).StoreSecret("SharedKey", "other-module");
        Scope(_moduleId).RetrieveSecret("SharedKey").Should().Be("first-module");
        Scope(otherId).RetrieveSecret("SharedKey").Should().Be("other-module");

        Scope(_moduleId).DeleteSecret("SharedKey");

        Scope(_moduleId).RetrieveSecret("SharedKey").Should().BeNull();
        Scope(otherId).RetrieveSecret("SharedKey").Should().Be("other-module");
    }

    [Fact]
    public void LauncherMigratesTheLegacySpotifyTokenAndLogoutCannotResurrectIt()
    {
        var launcherId = Guid.Parse("9b65a0b6-ce3e-4085-9ffa-b47c8fefcffd");
        const string legacyKey = "04399d2f-43c9-4182-b99d-2f43c97182a6:SpotifyRefreshToken";
        _storage.StoreSecret(legacyKey, "legacy-token");

        Scope(_moduleId).RetrieveSecret("SpotifyRefreshToken").Should().BeNull();
        Scope(launcherId).RetrieveSecret("SpotifyRefreshToken").Should().Be("legacy-token");
        _storage.RetrieveSecret($"{launcherId}:SpotifyRefreshToken").Should().Be("legacy-token");
        Scope(launcherId).StoreSecret("SpotifyRefreshToken", "new-token");
        Scope(launcherId).RetrieveSecret("SpotifyRefreshToken").Should().Be("new-token");

        Scope(launcherId).DeleteSecret("SpotifyRefreshToken");

        Scope(launcherId).RetrieveSecret("SpotifyRefreshToken").Should().BeNull();
        _storage.RetrieveSecret(legacyKey).Should().BeNull();
    }

    // This boundary stores state; the production scoping/migration code is the component under test.
    private sealed class MemoryStorage : ISecureStorageService
    {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);
        public void StoreSecret(string key, string secret) => _secrets[key] = secret;
        public string? RetrieveSecret(string key) => _secrets.GetValueOrDefault(key);
        public void DeleteSecret(string key) => _secrets.Remove(key);
    }
}
