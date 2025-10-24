namespace Axorith.Sdk.Services;

/// <summary>
/// Provides an abstraction for securely storing and retrieving sensitive data,
/// such as API tokens or passwords, using the operating system's protection mechanisms.
/// This service is provided by the Core to modules via dependency injection.
/// </summary>
public interface ISecureStorageService
{
    /// <summary>
    /// Encrypts and stores a secret value associated with a key.
    /// The key is automatically scoped to the calling module to prevent collisions.
    /// </summary>
    /// <param name="key">The unique key for the secret within the module (e.g., "AccessToken").</param>
    /// <param name="secret">The secret value to store.</param>
    void StoreSecret(string key, string secret);

    /// <summary>
    /// Retrieves and decrypts a secret value by its module-specific key.
    /// </summary>
    /// <param name="key">The key of the secret to retrieve.</param>
    /// <returns>The decrypted secret, or null if the key is not found.</returns>
    string? RetrieveSecret(string key);
}