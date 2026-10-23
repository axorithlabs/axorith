using System.Diagnostics;
using Axorith.Shared.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class ProcessBlockerLifecycleTests
{
    [Fact]
    public async Task PollingBlocksTheTargetAndUnblockAllStopsBlockingNewProcesses()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Directory.CreateTempSubdirectory("axorith-blocker-");
        var processName = $"axorith-probe-{Guid.NewGuid():N}";
        var executable = Path.Combine(directory.FullName, processName + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), executable);
        using var blocker = PlatformServices.CreateProcessBlocker(NullLogger.Instance);
        Process? probe = null;
        try
        {
            blocker.Block([processName]);
            Assert.True(blocker.IsMonitoring);
            probe = StartProbe();
            await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(probe.HasExited);
            probe.Dispose();
            probe = null;

            blocker.UnblockAll();
            Assert.False(blocker.IsMonitoring);
            probe = StartProbe();
            await Task.Delay(1200);
            Assert.False(probe.HasExited);
        }
        finally
        {
            blocker.UnblockAll();
            if (probe != null)
            {
                if (!probe.HasExited) probe.Kill();
                await probe.WaitForExitAsync();
                probe.Dispose();
            }
            var cleanup = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    directory.Delete(recursive: true);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException &&
                                           cleanup.Elapsed < TimeSpan.FromSeconds(5))
                {
                    await Task.Delay(50);
                }
            }
        }

        Process StartProbe() => Process.Start(new ProcessStartInfo(executable, "-t 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
    }
}
