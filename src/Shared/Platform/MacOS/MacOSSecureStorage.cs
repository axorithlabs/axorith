using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Axorith.Sdk.Services;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.MacOS;

/// <summary>
///     macOS-specific secure storage implementation using Keychain Services.
/// </summary>
[SupportedOSPlatform("macos")]
internal class MacOSSecureStorage : ISecureStorageService
{
    private readonly ILogger _logger;
    private const string ServiceName = "Axorith";

    public MacOSSecureStorage(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("Initialized macOS Keychain secure storage");
    }

    public void StoreSecret(string key, string secret)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or whitespace", nameof(key));
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Secret cannot be null or whitespace", nameof(secret));

        try
        {
            // Delete existing item first (if any)
            DeleteSecret(key);

            // Add new item to Keychain
            var secretBytes = Encoding.UTF8.GetBytes(secret);
            var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
            var accountBytes = Encoding.UTF8.GetBytes(key);

            var status = SecKeychainAddGenericPassword(
                IntPtr.Zero,                    // default keychain
                (uint)serviceBytes.Length,
                serviceBytes,
                (uint)accountBytes.Length,
                accountBytes,
                (uint)secretBytes.Length,
                secretBytes,
                IntPtr.Zero                     // don't need item reference
            );

            if (status != 0)
                throw new InvalidOperationException($"Failed to store secret in Keychain. Status: {status}");

            _logger.LogDebug("Stored secret for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing secret for key: {Key}", key);
            throw;
        }
    }

    public string? RetrieveSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or whitespace", nameof(key));

        try
        {
            var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
            var accountBytes = Encoding.UTF8.GetBytes(key);
            IntPtr passwordData = IntPtr.Zero;
            uint passwordLength = 0;
            IntPtr itemRef = IntPtr.Zero;

            var status = SecKeychainFindGenericPassword(
                IntPtr.Zero,                    // default keychain
                (uint)serviceBytes.Length,
                serviceBytes,
                (uint)accountBytes.Length,
                accountBytes,
                out passwordLength,
                out passwordData,
                out itemRef                     // don't need item reference but must pass out
            );

            if (status == -25300) // errSecItemNotFound
            {
                _logger.LogDebug("No secret found for key: {Key}", key);
                return null;
            }

            if (status != 0)
                throw new InvalidOperationException($"Failed to retrieve secret from Keychain. Status: {status}");

            if (passwordData == IntPtr.Zero || passwordLength == 0)
                return null;

            try
            {
                var secretBytes = new byte[passwordLength];
                Marshal.Copy(passwordData, secretBytes, 0, (int)passwordLength);
                return Encoding.UTF8.GetString(secretBytes);
            }
            finally
            {
                // Free the password data
                if (passwordData != IntPtr.Zero)
                    SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving secret for key: {Key}", key);
            throw;
        }
    }

    public void DeleteSecret(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or whitespace", nameof(key));

        try
        {
            var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
            var accountBytes = Encoding.UTF8.GetBytes(key);
            IntPtr itemRef = IntPtr.Zero;

            var status = SecKeychainFindGenericPassword(
                IntPtr.Zero,
                (uint)serviceBytes.Length,
                serviceBytes,
                (uint)accountBytes.Length,
                accountBytes,
                IntPtr.Zero,
                IntPtr.Zero,
                out itemRef
            );

            if (status == -25300) // errSecItemNotFound
            {
                _logger.LogDebug("No secret to delete for key: {Key}", key);
                return;
            }

            if (status != 0)
                throw new InvalidOperationException($"Failed to find secret in Keychain. Status: {status}");

            if (itemRef != IntPtr.Zero)
            {
                status = SecKeychainItemDelete(itemRef);
                CFRelease(itemRef);

                if (status != 0)
                    throw new InvalidOperationException($"Failed to delete secret from Keychain. Status: {status}");
            }

            _logger.LogDebug("Deleted secret for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting secret for key: {Key}", key);
            throw;
        }
    }

    #region Native Interop

    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        uint passwordLength,
        byte[] passwordData,
        IntPtr itemRef
    );

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        out uint passwordLength,
        out IntPtr passwordData,
        out IntPtr itemRef
    );

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        IntPtr passwordLength,
        IntPtr passwordData,
        out IntPtr itemRef
    );

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    #endregion
}
