using System.Diagnostics;
using System.Runtime.Versioning;

namespace Axorith.Shared.Platform.Linux;

[SupportedOSPlatform("linux")]
internal sealed class LinuxPlatformWindowService : LimitedPlatformWindowService
{
    public LinuxPlatformWindowService() : base("Linux") { }

    public override Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000,
        CancellationToken cancellationToken = default) =>
        LinuxWindowApi.WaitForWindowInitAsync(process, timeoutMs, cancellationToken);

    public override void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex) =>
        LinuxWindowApi.MoveWindowToMonitor(windowHandle, monitorIndex);
}
