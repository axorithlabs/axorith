using System.Diagnostics;
using Axorith.Client;
using Axorith.Client.Services;
using Axorith.Contracts;
using Axorith.Shared.Utils;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Axorith.Benchmarks;

public class HostReachabilityBenchmarks
{
    private HostController _hostController = null!;

    [GlobalSetup]
    public void Setup()
    {
        BenchmarkEnvironment.Initialize();
        EnsureNoRunningHost();

        var configuration = new Configuration();
        _hostController = new HostController(
            Options.Create(configuration),
            NullLogger<HostController>.Instance,
            new FileTokenProvider(NullLogger<FileTokenProvider>.Instance));
    }

    [Benchmark]
    [InvocationCount(1)]
    public async Task ProbeFreshProfileWithoutAuthToken()
    {
        var tokenPath = Path.Combine(ApplicationPaths.Config, AuthConstants.TokenFileName);
        if (File.Exists(tokenPath))
            throw new InvalidOperationException("The reachability benchmark profile must have no auth token.");

        var timer = Stopwatch.StartNew();
        if (await _hostController.IsHostReachableAsync())
            throw new InvalidOperationException("A Host without a profile auth token must not be reachable.");

        if (timer.Elapsed > TimeSpan.FromMilliseconds(100))
            throw new InvalidOperationException(
                $"A Host probe without an auth token took {timer.Elapsed.TotalMilliseconds:F1} ms.");
    }

    private static void EnsureNoRunningHost()
    {
        var hosts = Process.GetProcessesByName("Axorith.Host");
        try
        {
            if (hosts.Length > 0)
                throw new InvalidOperationException("Stop Axorith.Host before measuring cold reachability.");
        }
        finally
        {
            foreach (var host in hosts)
                host.Dispose();
        }

        try
        {
            using var mutex = Mutex.OpenExisting("Global\\AxorithHostInstanceMutex");
            throw new InvalidOperationException("Stop Axorith.Host before measuring cold reachability.");
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // No production Host owns the singleton mutex.
        }
    }
}
