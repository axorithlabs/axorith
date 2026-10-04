using Axorith.Sdk;
using Axorith.Sdk.Services;

namespace Axorith.Core.Services;

internal class ModuleScopedSecureStorage(ISecureStorageService underlyingStorage, ModuleDefinition moduleDefinition)
    : ISecureStorageService
{
    private const string SpotifyRefreshTokenKey = "SpotifyRefreshToken";
    private static readonly Guid ApplicationLauncherId = Guid.Parse("9b65a0b6-ce3e-4085-9ffa-b47c8fefcffd");
    private static readonly Guid LegacySpotifyModuleId = Guid.Parse("04399d2f-43c9-4182-b99d-2f43c97182a6");

    public void StoreSecret(string key, string secret)
    {
        var scopedKey = CreateScopedKey(key);
        underlyingStorage.StoreSecret(scopedKey, secret);
    }

    public string? RetrieveSecret(string key)
    {
        var scopedKey = CreateScopedKey(key);
        var secret = underlyingStorage.RetrieveSecret(scopedKey);
        if (secret != null || moduleDefinition.Id != ApplicationLauncherId || key != SpotifyRefreshTokenKey)
            return secret;

        var legacySecret = underlyingStorage.RetrieveSecret($"{LegacySpotifyModuleId}:{key}");
        if (legacySecret != null)
            underlyingStorage.StoreSecret(scopedKey, legacySecret);
        return legacySecret;
    }

    public void DeleteSecret(string key)
    {
        var scopedKey = CreateScopedKey(key);
        underlyingStorage.DeleteSecret(scopedKey);
        if (moduleDefinition.Id == ApplicationLauncherId && key == SpotifyRefreshTokenKey)
            underlyingStorage.DeleteSecret($"{LegacySpotifyModuleId}:{key}");
    }

    private string CreateScopedKey(string key)
    {
        // Example: "5fd185cb-21d0-4c2b-9185-cb21d03c2b8e:AccessToken"
        return $"{moduleDefinition.Id}:{key}";
    }
}
