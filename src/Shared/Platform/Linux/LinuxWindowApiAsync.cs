using System.Diagnostics;
using System.Runtime.Versioning;

namespace Axorith.Shared.Platform.Linux;

/// <summary>
///     Async wrapper for Linux window operations.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class LinuxWindowApiAsync
{
    /// <summary>
    ///     Waits for a process to create its main window.
    /// </summary>
    public static async Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.Now;

        while (true)
        {
            if (LinuxWindowApi.HasWindow(process.Id))
            {
                break;
            }

            if ((DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
            {
                throw new TimeoutException($"Process window did not appear within {timeoutMs}ms");
            }

            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(100, cancellationToken);
        }
    }
}
