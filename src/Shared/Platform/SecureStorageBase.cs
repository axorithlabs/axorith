using Axorith.Sdk.Services;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform;

internal abstract class SecureStorageBase(ILogger logger) : ISecureStorageService
{
    protected ILogger Logger { get; } = logger;

    public void StoreSecret(string key, string secret)
    {
        ValidateKey(key);
        ValidateSecret(secret);

        try
        {
            StoreSecretCore(key, secret);
            Logger.LogDebug("Stored secret for key: {Key}", key);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to store secret for key: {Key}", key);
            throw;
        }
    }

    public string? RetrieveSecret(string key)
    {
        ValidateKey(key);
        try
        {
            var secret = RetrieveSecretCore(key);
            if (secret == null)
                Logger.LogDebug("No secret found for key: {Key}", key);
            return secret;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to retrieve secret for key: {Key}", key);
            throw;
        }
    }

    public void DeleteSecret(string key)
    {
        ValidateKey(key);
        try
        {
            DeleteSecretCore(key);
            Logger.LogDebug("Deleted secret for key: {Key}", key);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to delete secret for key: {Key}", key);
            throw;
        }
    }

    protected virtual void ValidateSecret(string secret) => ArgumentNullException.ThrowIfNull(secret);

    protected abstract void StoreSecretCore(string key, string secret);
    protected abstract string? RetrieveSecretCore(string key);
    protected abstract void DeleteSecretCore(string key);

    private static void ValidateKey(string key) => ArgumentException.ThrowIfNullOrWhiteSpace(key);
}
