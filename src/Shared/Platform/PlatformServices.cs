using System.Diagnostics;
using System.Runtime.InteropServices;
using Axorith.Sdk.Services;
using Axorith.Shared.Platform.Linux;
using Axorith.Shared.Platform.MacOS;
using Axorith.Shared.Platform.Unix;
using Axorith.Shared.Platform.Windows;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform;

public static class PlatformServices
{
    public static IAutoStartManager CreateAutoStartManager(ILogger logger)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsAutoStartManager(logger);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxAutoStartManager(logger);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOsAutoStartManager(logger);
        }

        // Return no-op for unsupported platforms
        return new NoOpAutoStartManager();
    }

    public static ISecureStorageService CreateSecureStorage(ILogger logger)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsSecureStorage(logger);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxSecureStorage(logger);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOsSecureStorage(logger);
        }

        throw new PlatformNotSupportedException(
            $"Secure storage is not supported on this platform: {RuntimeInformation.OSDescription}");
    }

    public static IAppDiscoveryService CreateAppDiscoveryService(ILoggerFactory loggerFactory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsAppDiscoveryService(loggerFactory.CreateLogger<WindowsAppDiscoveryService>());
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxAppDiscoveryService(loggerFactory.CreateLogger<LinuxAppDiscoveryService>());
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOsAppDiscoveryService(loggerFactory.CreateLogger<MacOsAppDiscoveryService>());
        }

        throw new PlatformNotSupportedException(
            $"App discovery is not supported on this platform: {RuntimeInformation.OSDescription}");
    }

	public static IProcessBlocker CreateProcessBlocker(ILogger logger)
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return new WindowsProcessBlocker(logger);
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			return new LinuxProcessBlocker(logger);
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			return new MacOsProcessBlocker(logger);
		}

		throw new PlatformNotSupportedException(
			$"Process blocker is not supported on this platform: {RuntimeInformation.OSDescription}");
	}

    public static ISystemNotificationService CreateNotificationService(ILogger logger)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsSystemNotificationService(logger);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxSystemNotificationService(logger);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOsSystemNotificationService(logger);
        }

        return new NoOpNotificationService();
    }

    /// <summary>
    ///     Creates a platform-specific instance of INativeMessagingManager.
    /// </summary>
    public static INativeMessagingManager CreateNativeMessagingManager(ILoggerFactory loggerFactory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsNativeMessagingManager(loggerFactory.CreateLogger<WindowsNativeMessagingManager>());
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxNativeMessagingManager(loggerFactory.CreateLogger<LinuxNativeMessagingManager>());
        }

        throw new PlatformNotSupportedException(
            $"Native Messaging registration is not supported on this platform: {RuntimeInformation.OSDescription}");
    }

    public static IFilePermissionsService CreateFilePermissionsService(ILoggerFactory loggerFactory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsFilePermissionsService(loggerFactory.CreateLogger<WindowsFilePermissionsService>());
        }

        return new UnixFilePermissionsService(loggerFactory.CreateLogger<UnixFilePermissionsService>());
    }

    public static INamedPipeFactory CreateNamedPipeFactory(ILoggerFactory loggerFactory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsNamedPipeFactory(loggerFactory.CreateLogger<WindowsNamedPipeFactory>());
        }

        return new UnixNamedPipeFactory(loggerFactory.CreateLogger<UnixNamedPipeFactory>());
    }

    public static IPlatformWindowService CreateWindowService()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsPlatformWindowService();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxPlatformWindowService();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOsPlatformWindowService();
        }

        throw new PlatformNotSupportedException(
            $"Window management is not supported on this platform: {RuntimeInformation.OSDescription}");
    }

    public static IPlatformProcessService CreateProcessService()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsPlatformProcessService();
        }

        return new FallbackPlatformProcessService();
    }
}

// Simple fallback to avoid breaking Linux/Mac builds until implemented
file class NoOpNotificationService : ISystemNotificationService
{
    public Task ShowNotificationAsync(string title, string message, TimeSpan? expiration = null)
    {
        return Task.CompletedTask;
    }
}

file class NoOpAutoStartManager : IAutoStartManager
{
    public bool IsAutoStartEnabled => false;
    public bool IsStartMinimized => false;

    public bool EnableAutoStart(bool startMinimized = true)
    {
        return false;
    }

    public bool DisableAutoStart()
    {
        return true;
    }
}

file class FallbackPlatformProcessService : IPlatformProcessService
{
    public List<Process> FindProcesses(string processNameOrPath)
    {
        var processName = Path.GetFileNameWithoutExtension(processNameOrPath);
        return [.. Process.GetProcessesByName(processName)];
    }

    public bool IsProcessRunning(string processNameOrPath)
    {
        return FindProcesses(processNameOrPath).Count > 0;
    }

    public bool IsProcessRunningByName(string processName)
    {
        try
        {
            return Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            return false;
        }
    }
}