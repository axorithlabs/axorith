using Axorith.Sdk;
using Axorith.Sdk.Services;

namespace Axorith.Core.Services;

/// <summary>
///     An adapter that wraps an ISecureStorageService to provide an isolated,
///     sandboxed view for a specific module. It automatically prefixes all keys
///     with the module's unique ID to prevent data collisions.
/// </summary>
internal class ModuleScopedSecureStorage(ISecureStorageService underlyingStorage, ModuleDefinition moduleDefinition)
    : ISecureStorageService
{
    public void StoreSecret(string key, string secret)
    {
        var scopedKey = CreateScopedKey(key);
        underlyingStorage.StoreSecret(scopedKey, secret);
    }

    public string? RetrieveSecret(string key)
    {
        var scopedKey = CreateScopedKey(key);
        return underlyingStorage.RetrieveSecret(scopedKey);
    }

    public void DeleteSecret(string key)
    {
        var scopedKey = CreateScopedKey(key);
        underlyingStorage.DeleteSecret(scopedKey);
    }

    /// <summary>
    ///     Creates a globally unique key by combining the module's GUID and the user-provided key.
    /// </summary>
    private string CreateScopedKey(string key)
    {
        // Example: "5fd185cb-21d0-4c2b-9185-cb21d03c2b8e:AccessToken"
        return $"{moduleDefinition.Id}:{key}";
    }
}