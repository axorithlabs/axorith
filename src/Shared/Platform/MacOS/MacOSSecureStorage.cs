using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Axorith.Sdk.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace Axorith.Shared.Platform.MacOS;

/// <summary>
///     macOS-specific secure storage implementation using Keychain Services.
///     Uses modern SecItem* APIs (not deprecated SecKeychain* APIs).
/// </summary>
[SupportedOSPlatform("macos")]
internal class MacOsSecureStorage : ISecureStorageService
{
	private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";
	private const string CoreFoundationFramework = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
	private const string ServiceName = "com.axorith";

	// CFString constants created once at class load
	private static readonly IntPtr KSecClass = CreateStaticCfString("kSecClass");
	private static readonly IntPtr KSecClassGenericPassword = CreateStaticCfString("kSecClassGenericPassword");
	private static readonly IntPtr KSecAttrService = CreateStaticCfString("kSecAttrService");
	private static readonly IntPtr KSecAttrAccount = CreateStaticCfString("kSecAttrAccount");
	private static readonly IntPtr KSecValueData = CreateStaticCfString("kSecValueData");
	private static readonly IntPtr KSecReturnData = CreateStaticCfString("kSecReturnData");
	private static readonly IntPtr KSecMatchLimit = CreateStaticCfString("kSecMatchLimit");
	private static readonly IntPtr KSecMatchLimitOne = CreateStaticCfString("kSecMatchLimitOne");
	private static readonly IntPtr KCfBooleanTrue = new(1);

	private const int ErrSecSuccess = 0;
	private const int ErrSecItemNotFound = -25300;
	private const int ErrSecDuplicateItem = -25299;
	private const int ErrSecAuthFailed = -25293;
	private const int KCfStringEncodingUtf8 = 0x08000100;

	private readonly ILogger _logger;

	public MacOsSecureStorage(ILogger logger)
	{
		_logger = logger;
		_logger.LogInformation("Initialized macOS Keychain secure storage (SecItem* APIs)");
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
			// Delete existing item first (upsert pattern)
			DeleteExistingItem(key);

			var secretBytes = Encoding.UTF8.GetBytes(secret);

			var dataPtr = CFDataCreate(IntPtr.Zero, secretBytes, secretBytes.Length);
			var servicePtr = CFStringCreateWithCString(IntPtr.Zero, ServiceName, KCfStringEncodingUtf8);
			var accountPtr = CFStringCreateWithCString(IntPtr.Zero, key, KCfStringEncodingUtf8);

			var attributes = CreateMutableDictionary(6);
			CFDictionaryAddValue(attributes, KSecClass, KSecClassGenericPassword);
			CFDictionaryAddValue(attributes, KSecAttrService, servicePtr);
			CFDictionaryAddValue(attributes, KSecAttrAccount, accountPtr);
			CFDictionaryAddValue(attributes, KSecValueData, dataPtr);

			var status = SecItemAdd(attributes, out _);

			// Release temporary CF objects
			CFRelease(dataPtr);
			CFRelease(servicePtr);
			CFRelease(accountPtr);
			CFRelease(attributes);

			if (status != ErrSecSuccess)
			{
				throw new InvalidOperationException($"Failed to store secret in Keychain. OSStatus: {status}");
			}

			_logger.LogDebug("Stored secret for key: {Key}", key);
		}
		catch (Exception ex) when (ex is not InvalidOperationException)
		{
			_logger.LogError(ex, "Error storing secret for key: {Key}", key);
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
			var servicePtr = CFStringCreateWithCString(IntPtr.Zero, ServiceName, KCfStringEncodingUtf8);
			var accountPtr = CFStringCreateWithCString(IntPtr.Zero, key, KCfStringEncodingUtf8);

			var query = CreateMutableDictionary(5);
			CFDictionaryAddValue(query, KSecClass, KSecClassGenericPassword);
			CFDictionaryAddValue(query, KSecAttrService, servicePtr);
			CFDictionaryAddValue(query, KSecAttrAccount, accountPtr);
			CFDictionaryAddValue(query, KSecReturnData, KCfBooleanTrue);
			CFDictionaryAddValue(query, KSecMatchLimit, KSecMatchLimitOne);

			var status = SecItemCopyMatching(query, out var resultHandle);

			// Release query and temp strings
			CFRelease(query);
			CFRelease(servicePtr);
			CFRelease(accountPtr);

			switch (status)
            {
                case ErrSecItemNotFound:
                    _logger.LogDebug("No secret found for key: {Key}", key);
                    return null;
                case ErrSecAuthFailed:
                    _logger.LogWarning("Keychain access denied for key: {Key}. User may have denied access.", key);
                    throw new UnauthorizedAccessException($"Keychain access denied for key: {key}");
            }

            if (status != ErrSecSuccess)
			{
				throw new InvalidOperationException($"Failed to retrieve secret from Keychain. OSStatus: {status}");
			}

			// Wrap raw IntPtr in SafeHandle for atomic resource cleanup
			using var safeHandle = new CfSafeHandle(resultHandle, true);
			try
			{
				var length = CFDataGetLength(safeHandle.DangerousGetHandle());
				if (length <= 0)
				{
					return null;
				}

				var buffer = new byte[length];
				var dataPtr = CFDataGetBytePtr(safeHandle.DangerousGetHandle());
				Marshal.Copy(dataPtr, buffer, 0, length);
				return Encoding.UTF8.GetString(buffer);
			}
			finally
			{
				safeHandle.Dispose();
			}
		}
		catch (Exception ex) when (ex is not InvalidOperationException and not UnauthorizedAccessException)
		{
			_logger.LogError(ex, "Error retrieving secret for key: {Key}", key);
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
			DeleteExistingItem(key);
			_logger.LogDebug("Deleted secret for key: {Key}", key);
		}
		catch (Exception ex) when (ex is not InvalidOperationException)
		{
			_logger.LogError(ex, "Error deleting secret for key: {Key}", key);
			throw;
		}
	}

	private void DeleteExistingItem(string key)
	{
		var servicePtr = CFStringCreateWithCString(IntPtr.Zero, ServiceName, KCfStringEncodingUtf8);
		var accountPtr = CFStringCreateWithCString(IntPtr.Zero, key, KCfStringEncodingUtf8);

		var query = CreateMutableDictionary(3);
		CFDictionaryAddValue(query, KSecClass, KSecClassGenericPassword);
		CFDictionaryAddValue(query, KSecAttrService, servicePtr);
		CFDictionaryAddValue(query, KSecAttrAccount, accountPtr);

		var status = SecItemDelete(query);

		CFRelease(query);
		CFRelease(servicePtr);
		CFRelease(accountPtr);

		if (status != ErrSecSuccess && status != ErrSecItemNotFound)
		{
			throw new InvalidOperationException($"Failed to delete secret from Keychain. OSStatus: {status}");
		}
	}

	/// <summary>
	///     SafeHandle for CoreFoundation data objects. Calls CFRelease on dispose.
	/// </summary>
	internal sealed class CfSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
	{
		public CfSafeHandle() : base(true) { }

		public CfSafeHandle(IntPtr preexistingHandle, bool ownsHandle) : base(ownsHandle)
		{
			SetHandle(preexistingHandle);
		}

		protected override bool ReleaseHandle()
		{
			if (!IsInvalid)
			{
				CFRelease(handle);
			}
			return true;
		}
	}

	/// <summary>
	///     Creates a mutable CFDictionary with default key/value callbacks.
	/// </summary>
	private static IntPtr CreateMutableDictionary(int capacity)
	{
		var ptr = CFDictionaryCreateMutable(IntPtr.Zero, capacity, IntPtr.Zero, IntPtr.Zero);
		return ptr == IntPtr.Zero ? throw new OutOfMemoryException("Failed to create mutable CFDictionary") : ptr;
    }

	/// <summary>
	///     Creates a CFString that lives for the lifetime of the class (used for constants).
	/// </summary>
	private static IntPtr CreateStaticCfString(string value)
	{
		var ptr = CFStringCreateWithCString(IntPtr.Zero, value, KCfStringEncodingUtf8);
		if (ptr == IntPtr.Zero)
		{
			throw new OutOfMemoryException($"Failed to create static CFString from '{value}'");
		}
		return ptr;
	}

	[DllImport(SecurityFramework, EntryPoint = "SecItemAdd")]
	private static extern int SecItemAdd(IntPtr attributes, out IntPtr result);

	[DllImport(SecurityFramework, EntryPoint = "SecItemCopyMatching")]
	private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

	[DllImport(SecurityFramework, EntryPoint = "SecItemUpdate")]
	private static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

	[DllImport(SecurityFramework, EntryPoint = "SecItemDelete")]
	private static extern int SecItemDelete(IntPtr query);

	[DllImport(CoreFoundationFramework)]
	private static extern IntPtr CFDictionaryCreateMutable(
		IntPtr allocator,
		int capacity,
		IntPtr keyCallBacks,
		IntPtr valueCallBacks
	);

	[DllImport(CoreFoundationFramework)]
	private static extern void CFDictionaryAddValue(IntPtr theDict, IntPtr key, IntPtr value);

	[DllImport(CoreFoundationFramework)]
	private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, int encoding);

	[DllImport(CoreFoundationFramework)]
	private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, int length);

	[DllImport(CoreFoundationFramework)]
	private static extern int CFDataGetLength(IntPtr theData);

	[DllImport(CoreFoundationFramework)]
	private static extern IntPtr CFDataGetBytePtr(IntPtr theData);

	[DllImport(CoreFoundationFramework)]
	private static extern void CFRelease(IntPtr cf);
}
