using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.MacOS;

[SupportedOSPlatform("macos")]
internal class MacOsSecureStorage : SecureStorageBase
{
    private const string ServiceName = "Axorith";

    public MacOsSecureStorage(ILogger logger) : base(logger)
    {
        Logger.LogInformation("Initialized macOS Keychain secure storage");
    }

    protected override void ValidateSecret(string secret) => ArgumentException.ThrowIfNullOrWhiteSpace(secret);

    protected override void StoreSecretCore(string key, string secret)
    {
        DeleteSecretCore(key);

        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
        var accountBytes = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainAddGenericPassword(IntPtr.Zero, (uint)serviceBytes.Length, serviceBytes,
            (uint)accountBytes.Length, accountBytes, (uint)secretBytes.Length, secretBytes, IntPtr.Zero);

        if (status != 0)
            throw new InvalidOperationException($"Failed to store secret in Keychain. Status: {status}");
    }

    protected override string? RetrieveSecretCore(string key)
    {
        var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
        var accountBytes = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)serviceBytes.Length, serviceBytes,
            (uint)accountBytes.Length, accountBytes, out var passwordLength, out var passwordData, out _);

        if (status == -25300)
            return null;
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
            SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
        }
    }

    protected override void DeleteSecretCore(string key)
    {
        var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
        var accountBytes = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)serviceBytes.Length, serviceBytes,
            (uint)accountBytes.Length, accountBytes, IntPtr.Zero, IntPtr.Zero, out var itemRef);

        if (status == -25300)
            return;
        if (status != 0)
            throw new InvalidOperationException($"Failed to find secret in Keychain. Status: {status}");
        if (itemRef == IntPtr.Zero)
            return;

        try
        {
            status = SecKeychainItemDelete(itemRef);
            if (status != 0)
                throw new InvalidOperationException($"Failed to delete secret from Keychain. Status: {status}");
        }
        finally
        {
            CFRelease(itemRef);
        }
    }

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
}
