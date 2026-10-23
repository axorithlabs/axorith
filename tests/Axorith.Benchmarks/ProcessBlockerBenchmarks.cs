using Axorith.Shared.Platform;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Benchmarks;

[MemoryDiagnoser]
public class ProcessBlockerBenchmarks
{
    private IProcessBlocker _blocker = null!;
    private readonly string _target = $"axorith-absent-{Guid.NewGuid():N}";

    [GlobalSetup]
    public void Setup()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("This benchmark measures the Windows process scanner.");
        _blocker = PlatformServices.CreateProcessBlocker(NullLogger.Instance);
    }

    [Benchmark]
    public void ScanBlockListAndRenamedProcesses()
    {
        _blocker.Block([_target]);
        _blocker.UnblockAll();
    }

    [GlobalCleanup]
    public void Cleanup() => _blocker.Dispose();
}
