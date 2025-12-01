using Axorith.Core.Services;
using BenchmarkDotNet.Attributes;

namespace Axorith.Benchmarks;

/// <summary>
///     Benchmarks for EventAggregator publish/subscribe performance.
///     Measures latency and throughput for event-driven communication.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class EventAggregatorBenchmarks
{
    private EventAggregator _aggregator = null!;
    private List<IDisposable> _subscriptions = null!;

    public record TestEvent(int Id, string Message);

    public record LargeEvent(int Id, string Message, byte[] Data);

    [GlobalSetup]
    public void Setup()
    {
        _aggregator = new EventAggregator();
        _subscriptions = [];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }
    }

    [Benchmark(Baseline = true)]
    public void PublishWithNoSubscribers()
    {
        _aggregator.Publish(new TestEvent(1, "test"));
    }

    [Benchmark]
    public void SubscribeAndUnsubscribe()
    {
        var sub = _aggregator.Subscribe<TestEvent>(_ => { });
        sub.Dispose();
    }

    [Benchmark]
    [Arguments(1)]
    [Arguments(10)]
    [Arguments(100)]
    public void PublishToSubscribers(int subscriberCount)
    {
        var aggregator = new EventAggregator();
        var subs = new List<IDisposable>();
        var receivedCount = 0;

        for (var i = 0; i < subscriberCount; i++)
        {
            subs.Add(aggregator.Subscribe<TestEvent>(_ => receivedCount++));
        }

        aggregator.Publish(new TestEvent(1, "test"));

        foreach (var sub in subs)
        {
            sub.Dispose();
        }
    }

    [Benchmark]
    [Arguments(10)]
    [Arguments(100)]
    [Arguments(1000)]
    public void MultiplePublishes(int count)
    {
        var aggregator = new EventAggregator();
        var receivedCount = 0;
        var sub = aggregator.Subscribe<TestEvent>(_ => receivedCount++);

        for (var i = 0; i < count; i++)
        {
            aggregator.Publish(new TestEvent(i, "test"));
        }

        sub.Dispose();
    }

    [Benchmark]
    public async Task PublishAsync()
    {
        var aggregator = new EventAggregator();
        var receivedCount = 0;
        var sub = aggregator.Subscribe<TestEvent>(_ => receivedCount++);

        await aggregator.PublishAsync(new TestEvent(1, "test"));

        sub.Dispose();
    }

    [Benchmark]
    [Arguments(10)]
    [Arguments(100)]
    public async Task MultipleAsyncPublishes(int count)
    {
        var aggregator = new EventAggregator();
        var receivedCount = 0;
        var sub = aggregator.Subscribe<TestEvent>(_ => receivedCount++);

        for (var i = 0; i < count; i++)
        {
            await aggregator.PublishAsync(new TestEvent(i, "test"));
        }

        sub.Dispose();
    }

    [Benchmark]
    public void PublishLargeEvent()
    {
        var aggregator = new EventAggregator();
        var data = new byte[1024]; // 1KB payload
        var sub = aggregator.Subscribe<LargeEvent>(_ => { });

        aggregator.Publish(new LargeEvent(1, "test", data));

        sub.Dispose();
    }

    [Benchmark]
    public void ConcurrentPublishAndSubscribe()
    {
        var aggregator = new EventAggregator();
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Parallel.For(0, 10, i =>
        {
            if (i % 2 == 0)
            {
                var sub = aggregator.Subscribe<TestEvent>(_ => { });
                Task.Delay(10).Wait();
                sub.Dispose();
            }
            else
            {
                aggregator.Publish(new TestEvent(i, "test"));
            }
        });
    }

    [Benchmark]
    public void WeakReferenceCleanup()
    {
        var aggregator = new EventAggregator();

        // Create subscribers that will be GC'd
        for (var i = 0; i < 100; i++)
        {
            aggregator.Subscribe<TestEvent>(_ => { });
        }

        // Force GC
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Publish should trigger cleanup
        aggregator.Publish(new TestEvent(1, "test"));
    }
}

