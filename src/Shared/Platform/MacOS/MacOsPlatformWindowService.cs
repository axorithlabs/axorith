using System.Diagnostics;
using System.Runtime.Versioning;

namespace Axorith.Shared.Platform.MacOS;

[SupportedOSPlatform("macos")]
internal sealed class MacOsPlatformWindowService : LimitedPlatformWindowService
{
    public MacOsPlatformWindowService() : base("macOS") { }

    public override Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000,
        CancellationToken cancellationToken = default) =>
        MacOsWindowApi.WaitForWindowInitAsync(process, timeoutMs, cancellationToken);

    public override void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex) =>
        MacOsWindowApi.MoveWindowToMonitor(windowHandle, monitorIndex);
}
