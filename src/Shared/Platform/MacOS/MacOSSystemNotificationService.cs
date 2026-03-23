using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.MacOS;

/// <summary>
///     macOS implementation of ISystemNotificationService using UNUserNotificationCenter.
///     Uses Objective-C runtime P/Invoke to interact with UserNotifications.framework.
///     Gracefully degrades if notification permission is denied or framework unavailable.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacOsSystemNotificationService : ISystemNotificationService
{
	private readonly ILogger _logger;
	private readonly IntPtr _notificationCenter;

	private const string ObjCRuntime = "/usr/lib/libobjc.dylib";
	private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

	// Authorization status values (NSInteger from UNAuthorizationStatus enum)
	private const int AuthorizationStatusNotDetermined = 0;
	private const int AuthorizationStatusDenied = 1;
	private const int AuthorizationStatusAuthorized = 2;
	private const int AuthorizationStatusProvisional = 3;

	// UNAuthorizationOptions bit flags
	private const int AuthOptionAlert = 1 << 0; // 1
	private const int AuthOptionSound = 1 << 1; // 2
	private const int AuthOptionBadge = 1 << 2; // 4

	// Delegate types for Objective-C message sends
	// Signature: id objc_msgSend(id receiver, SEL selector, ...)
	private delegate IntPtr ObjCSend2(IntPtr receiver, IntPtr selector);
	private delegate IntPtr ObjCSend3(IntPtr receiver, IntPtr selector, IntPtr arg);
	private delegate IntPtr ObjCSend4(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);
	private delegate IntPtr ObjCSend5(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

	// Cached objc_msgSend delegates (loaded once, reused across instances)
	private static readonly ObjCSend2 MsgSend2;
	private static readonly ObjCSend3 MsgSend3;
	private static readonly ObjCSend4 MsgSend4;
	private static readonly ObjCSend5 MsgSend5;

	static MacOsSystemNotificationService()
	{
		var handle = NativeLibrary.Load(ObjCRuntime);
		var msgSendPtr = NativeLibrary.GetExport(handle, "objc_msgSend");
		MsgSend2 = Marshal.GetDelegateForFunctionPointer<ObjCSend2>(msgSendPtr);
		MsgSend3 = Marshal.GetDelegateForFunctionPointer<ObjCSend3>(msgSendPtr);
		MsgSend4 = Marshal.GetDelegateForFunctionPointer<ObjCSend4>(msgSendPtr);
		MsgSend5 = Marshal.GetDelegateForFunctionPointer<ObjCSend5>(msgSendPtr);
	}

	public MacOsSystemNotificationService(ILogger logger)
	{
		_logger = logger;
		_notificationCenter = GetSharedNotificationCenter();

		if (_notificationCenter == IntPtr.Zero)
		{
			_logger.LogWarning("UNUserNotificationCenter not available on this system");
			return;
		}

		try
		{
			RequestAuthorizationNonBlocking();
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to request notification authorization — notifications may not appear");
		}
	}

	public Task ShowNotificationAsync(string title, string message, TimeSpan? expiration = null)
	{
		if (_notificationCenter == IntPtr.Zero)
		{
			_logger.LogDebug("Notification skipped (center unavailable): {Title}", title);
			return Task.CompletedTask;
		}

		try
		{
			var status = GetAuthorizationStatus();
			if (status == AuthorizationStatusDenied)
			{
				_logger.LogWarning("Notification permission denied — skipping: {Title}", title);
				return Task.CompletedTask;
			}

			SendNotification(title, message);
			_logger.LogDebug("Notification dispatched: {Title}", title);
		}
		catch (Exception ex)
		{
			// Never throw — graceful degradation matches Linux/Windows pattern
			_logger.LogWarning(ex, "Failed to show notification: {Title}", title);
		}

		return Task.CompletedTask;
	}

	private static IntPtr GetSharedNotificationCenter()
	{
		var centerClass = ObjCGetClass("UNUserNotificationCenter");
		var sharedSel = ObjCSel("currentNotificationCenter");
		return MsgSend2(centerClass, sharedSel);
	}

	private int GetAuthorizationStatus()
	{
		var settingsSel = ObjCSel("notificationSettings");
		var settings = MsgSend2(_notificationCenter, settingsSel);

		var authStatusSel = ObjCSel("authorizationStatus");
		var status = MsgSend2(settings, authStatusSel);

		CFRelease(settings);
		return (int)status;
	}

	private void RequestAuthorizationNonBlocking()
	{
		var options = (IntPtr)(AuthOptionAlert | AuthOptionSound | AuthOptionBadge);

		var requestSel = ObjCSel("requestAuthorizationWithOptions:completionHandler:");
		// Pass IntPtr.Zero for completionHandler — fire-and-forget
		MsgSend4(_notificationCenter, requestSel, options, IntPtr.Zero);
	}

	private void SendNotification(string title, string body)
	{
		var contentClass = ObjCGetClass("UNMutableNotificationContent");
		var content = MsgSend2(MsgSend2(contentClass, ObjCSel("alloc")), ObjCSel("init"));

		try
		{
			// Set title
			var titleNsString = CreateNsString(title);
			MsgSend3(content, ObjCSel("setTitle:"), titleNsString);
			CFRelease(titleNsString);

			// Set body
			var bodyNsString = CreateNsString(body);
			MsgSend3(content, ObjCSel("setBody:"), bodyNsString);
			CFRelease(bodyNsString);

		// Set sound (default notification sound)
		// defaultSound returns a shared singleton — do NOT CFRelease
		var soundClass = ObjCGetClass("UNNotificationSound");
		var sound = MsgSend2(soundClass, ObjCSel("defaultSound"));
		MsgSend3(content, ObjCSel("setSound:"), sound);

		// Create unique notification identifier
		var idNsString = CreateNsString($"axorith-{Guid.NewGuid()}");

		// Create UNNotificationRequest with nil trigger (immediate delivery)
		// requestWithIdentifier:content:trigger: is a convenience constructor
		// returning an autoreleased object. We retain it so CFRelease below is balanced.
		var requestClass = ObjCGetClass("UNNotificationRequest");
		var factorySel = ObjCSel("requestWithIdentifier:content:trigger:");
		var request = MsgSend5(
			requestClass,
			factorySel,
			idNsString,
			content,
			IntPtr.Zero // trigger = nil → immediate delivery
		);
		CFRetain(request);

			// Add request to notification center
			MsgSend4(_notificationCenter, ObjCSel("addNotificationRequest:withCompletionHandler:"),
				request, IntPtr.Zero);

			// Cleanup CoreFoundation objects
			CFRelease(idNsString);
			CFRelease(request);
		}
		finally
		{
			CFRelease(content);
		}
	}

	private static IntPtr CreateNsString(string str)
	{
		var nsStringClass = ObjCGetClass("NSString");
		var allocSel = ObjCSel("alloc");
		var initSel = ObjCSel("initWithUTF8String:");

		var utf8 = Encoding.UTF8.GetBytes(str);
		var ptr = Marshal.AllocHGlobal(utf8.Length + 1);
		try
		{
			Marshal.Copy(utf8, 0, ptr, utf8.Length);
			Marshal.WriteByte(ptr, utf8.Length, 0);

			return MsgSend3(MsgSend2(nsStringClass, allocSel), initSel, ptr);
		}
		finally
		{
			Marshal.FreeHGlobal(ptr);
		}
	}

	[DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

	[DllImport(ObjCRuntime, CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

	[DllImport(CoreFoundation)]
	private static extern void CFRelease(IntPtr cf);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFRetain(IntPtr cf);

	// Type-safe wrappers for ObjC runtime functions
	private static IntPtr ObjCGetClass(string name) => objc_getClass(name);
	private static IntPtr ObjCSel(string name) => sel_registerName(name);
}
