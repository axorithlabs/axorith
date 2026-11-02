using Axorith.Core.Services;
using BenchmarkDotNet.Attributes;

namespace Axorith.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class EventAggregatorBenchmarks
{
    private EventAggregator _aggregator = null!;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly TestEvent _event = new(1, "test");
    private int _received;

    public record TestEvent(int Id, string Message);

    [Params(0, 1, 10, 100)]
    public int SubscriberCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _aggregator = new EventAggregator();
        for (var index = 0; index < SubscriberCount; index++)
            _subscriptions.Add(_aggregator.Subscribe<TestEvent>(_ => Interlocked.Increment(ref _received)));
    }

    [Benchmark]
    public void Publish()
    {
        _received = 0;
        _aggregator.Publish(_event);
        VerifyDelivery();
    }

    [Benchmark]
    public async Task PublishAsync()
    {
        _received = 0;
        await _aggregator.PublishAsync(_event);
        VerifyDelivery();
    }

    [Benchmark]
    public void SubscribeAndUnsubscribe()
    {
        var received = 0;
        var subscription = _aggregator.Subscribe<int>(_ => received++);
        _aggregator.Publish(42);
        subscription.Dispose();
        _aggregator.Publish(43);
        if (received != 1) throw new InvalidOperationException("Subscription lifetime was not respected.");
    }

    private void VerifyDelivery()
    {
        if (_received != SubscriberCount) throw new InvalidOperationException("Events were lost.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var subscription in _subscriptions) subscription.Dispose();
        _subscriptions.Clear();
    }
}
