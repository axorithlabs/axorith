using Axorith.Shared.Utils.Windows;

namespace Axorith.Shared.Utils;

public static class DeviceIdProvider
{
    public static string GetDeviceId() => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299)
        ? WindowsDeviceIdentity.GetDeviceId()
        : throw new PlatformNotSupportedException("Device identity is currently supported only on Windows 10 1709 or later.");
}
