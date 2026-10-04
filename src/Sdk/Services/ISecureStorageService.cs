namespace Axorith.Sdk.Services;

/// <summary>
///     Provides an abstraction for securely storing and retrieving sensitive data,
///     such as API tokens or passwords, using the operating system's protection mechanisms.
///     This service is provided by the Core to modules via dependency injection.
/// </summary>
public interface ISecureStorageService
{
    /// <summary>
    ///     Encrypts and stores a secret value associated with a key.
    ///     The key is automatically scoped to the calling module to prevent collisions.
    /// </summary>
    void StoreSecret(string key, string secret);

    /// <summary>
    ///     Retrieves and decrypts a secret value by its module-specific key.
    /// </summary>
    string? RetrieveSecret(string key);

    /// <summary>
    ///     Deletes a secret value by its module-specific key.
    ///     Useful for logout scenarios where tokens should be cleared.
    /// </summary>
    void DeleteSecret(string key);
}
