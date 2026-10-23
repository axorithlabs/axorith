using System.Diagnostics;

namespace Axorith.Shared.Platform;

internal static class WindowWaiter
{
    public static async Task WaitAsync(Process process, Func<Process, bool> hasWindow, int timeoutMs,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (process.HasExited)
                    throw new TimeoutException($"Process {process.Id} exited before its window appeared.");
                if (hasWindow(process)) return;
            }
            catch (InvalidOperationException ex) when (process.HasExited)
            {
                throw new TimeoutException($"Process {process.Id} exited before its window appeared.", ex);
            }

            if (Stopwatch.GetElapsedTime(started) > timeout)
                throw new TimeoutException($"Process window did not appear within {timeoutMs}ms");

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }
}
