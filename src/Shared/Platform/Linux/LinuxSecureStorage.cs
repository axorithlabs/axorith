using System.Runtime.Versioning;
using System.Text;
using Axorith.Sdk.Services;
using DBus.Services.Secrets;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Linux;

[SupportedOSPlatform("linux")]
internal sealed class LinuxSecureStorage : ISecureStorageService
{
    private readonly ILogger _logger;
    private readonly SecretService _secretService;
    private Collection? _defaultCollection;

    private const string AppLabel = "Axorith";

    public LinuxSecureStorage(ILogger logger)
    {
        _logger = logger;

        try
        {
            _secretService = SecretService.ConnectAsync(EncryptionType.Dh).GetAwaiter().GetResult();
            _logger.LogInformation("Secure storage initialized via D-Bus Secret Service (encrypted session)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to connect to D-Bus Secret Service. " +
                "Install gnome-keyring for secure credential storage. " +
                "On Ubuntu/Debian: sudo apt install gnome-keyring");
            throw new InvalidOperationException(
                "D-Bus Secret Service is not available. Install gnome-keyring: sudo apt install gnome-keyring",
                ex);
        }
    }

    public void StoreSecret(string key, string secret)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("Secret cannot be null or whitespace", nameof(secret));
        }

        try
        {
            var collection = GetDefaultCollection();

            var lookupAttributes = new Dictionary<string, string>
            {
                { "application", AppLabel.ToLowerInvariant() },
                { "key", key }
            };

            var secretBytes = Encoding.UTF8.GetBytes(secret);
            var contentType = "text/plain; charset=utf8";

            var item = collection.CreateItemAsync(
                $"{AppLabel}:{key}",
                lookupAttributes,
                secretBytes,
                contentType,
                replace: true).GetAwaiter().GetResult();

            if (item == null)
            {
                throw new InvalidOperationException(
                    $"Failed to store secret for key '{key}'. The keyring may be locked or the user denied access.");
            }

            _logger.LogDebug("Stored secret via D-Bus Secret Service for key: {Key}", key);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to store secret via D-Bus for key: {Key}", key);
            throw;
        }
    }

    public string? RetrieveSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace", nameof(key));
        }

        try
        {
            var collection = GetDefaultCollection();

            var lookupAttributes = new Dictionary<string, string>
            {
                { "application", AppLabel.ToLowerInvariant() },
                { "key", key }
            };

            var matchedItems = collection.SearchItemsAsync(lookupAttributes).GetAwaiter().GetResult();

            foreach (var item in matchedItems)
            {
                try
                {
                    var secretBytes = item.GetSecretAsync().GetAwaiter().GetResult();
                    if (secretBytes.Length > 0)
                    {
                        var result = Encoding.UTF8.GetString(secretBytes);
                        _logger.LogDebug("Retrieved secret via D-Bus Secret Service for key: {Key}", key);
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read secret from matched item for key: {Key}", key);
                }
            }

            _logger.LogDebug("No secret found for key: {Key}", key);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve secret via D-Bus for key: {Key}", key);
            throw;
        }
    }

    public void DeleteSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace", nameof(key));
        }

        try
        {
            var collection = GetDefaultCollection();

            var lookupAttributes = new Dictionary<string, string>
            {
                { "application", AppLabel.ToLowerInvariant() },
                { "key", key }
            };

            var matchedItems = collection.SearchItemsAsync(lookupAttributes).GetAwaiter().GetResult();

            foreach (var item in matchedItems)
            {
                try
                {
                    item.DeleteAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete secret item for key: {Key}", key);
                }
            }

            _logger.LogDebug("Deleted secret via D-Bus Secret Service for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete secret via D-Bus for key: {Key}", key);
            throw;
        }
    }

    private Collection GetDefaultCollection()
    {
        if (_defaultCollection != null)
        {
            return _defaultCollection;
        }

        _defaultCollection = _secretService.GetDefaultCollectionAsync().GetAwaiter().GetResult();

        if (_defaultCollection == null)
        {
            throw new InvalidOperationException(
                "Default keyring collection is not available. " +
                "Ensure gnome-keyring is running: gnome-keyring-daemon --start");
        }

        _logger.LogDebug("Using default Secret Service collection");
        return _defaultCollection;
    }
}
