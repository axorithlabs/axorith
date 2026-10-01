using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk.Services;
using FluentAssertions;

namespace Axorith.Core.Tests.Services;

public class EventAggregatorTests
{
    private static IEventAggregator CreateAggregator() => new EventAggregator();

    private class TestEvent
    {
        public string Message { get; init; } = string.Empty;
    }

    private class AnotherEvent
    {
        public int Value { get; init; }
    }

    [Fact]
    public void Subscribe_ShouldReceivePublishedEvents()
    {
        // Arrange
        var aggregator = CreateAggregator();
        TestEvent? receivedEvent = null;
        aggregator.Subscribe<TestEvent>(e => receivedEvent = e);

        // Act
        var publishedEvent = new TestEvent { Message = "Hello" };
        aggregator.Publish(publishedEvent);

        // Assert
        receivedEvent.Should().NotBeNull();
        receivedEvent!.Message.Should().Be("Hello");
    }

    [Fact]
    public void Subscribe_MultipleHandlers_ShouldAllReceiveEvent()
    {
        // Arrange
        var aggregator = CreateAggregator();
        var handler1Received = false;
        var handler2Received = false;
        var handler3Received = false;

        aggregator.Subscribe<TestEvent>(_ => handler1Received = true);
        aggregator.Subscribe<TestEvent>(_ => handler2Received = true);
        aggregator.Subscribe<TestEvent>(_ => handler3Received = true);

        // Act
        aggregator.Publish(new TestEvent());

        // Assert
        handler1Received.Should().BeTrue();
        handler2Received.Should().BeTrue();
        handler3Received.Should().BeTrue();
    }

    [Fact]
    public void Publish_WithoutSubscribers_ShouldNotThrow()
    {
        // Arrange
        var aggregator = CreateAggregator();

        // Act
        var act = () => aggregator.Publish(new TestEvent { Message = "No subscribers" });

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Unsubscribe_ShouldStopReceivingEvents()
    {
        // Arrange
        var aggregator = CreateAggregator();
        var receivedCount = 0;
        var subscription = aggregator.Subscribe<TestEvent>(_ => receivedCount++);

        // Act
        aggregator.Publish(new TestEvent()); // Should receive
        subscription.Dispose();
        aggregator.Publish(new TestEvent()); // Should not receive

        // Assert
        receivedCount.Should().Be(1);
    }

    [Fact]
    public void Subscribe_DifferentEventTypes_ShouldOnlyReceiveMatchingType()
    {
        // Arrange
        var aggregator = CreateAggregator();
        TestEvent? testEvent = null;
        AnotherEvent? anotherEvent = null;

        aggregator.Subscribe<TestEvent>(e => testEvent = e);
        aggregator.Subscribe<AnotherEvent>(e => anotherEvent = e);

        // Act
        aggregator.Publish(new TestEvent { Message = "Test" });
        aggregator.Publish(new AnotherEvent { Value = 42 });

        // Assert
        testEvent.Should().NotBeNull();
        testEvent!.Message.Should().Be("Test");
        anotherEvent.Should().NotBeNull();
        anotherEvent!.Value.Should().Be(42);
    }

    [Fact]
    public void Subscribe_HandlerThrowsException_ShouldNotBreakOtherHandlers()
    {
        // Arrange
        var aggregator = CreateAggregator();
        var handler1Executed = false;
        var handler3Executed = false;

        aggregator.Subscribe<TestEvent>(_ => handler1Executed = true);
        aggregator.Subscribe<TestEvent>(_ => throw new InvalidOperationException("Handler error"));
        aggregator.Subscribe<TestEvent>(_ => handler3Executed = true);

        // Act
        aggregator.Publish(new TestEvent());

        // Assert
        handler1Executed.Should().BeTrue();
        handler3Executed.Should().BeTrue();
    }

    [Fact]
    public void MultipleUnsubscribe_ShouldBeIdempotent()
    {
        // Arrange
        var aggregator = CreateAggregator();
        var subscription = aggregator.Subscribe<TestEvent>(_ => { });

        // Act
        subscription.Dispose();
        var act = () => subscription.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Publish_WithMultipleEventTypes_ShouldIsolateThem()
    {
        // Arrange
        var aggregator = CreateAggregator();
        var testEventCount = 0;
        var anotherEventCount = 0;

        aggregator.Subscribe<TestEvent>(_ => testEventCount++);
        aggregator.Subscribe<AnotherEvent>(_ => anotherEventCount++);

        // Act
        aggregator.Publish(new TestEvent());
        aggregator.Publish(new TestEvent());
        aggregator.Publish(new AnotherEvent());

        // Assert
        testEventCount.Should().Be(2);
        anotherEventCount.Should().Be(1);
    }

    [Fact]
    public void Subscribe_SameHandlerMultipleTimes_ShouldReceiveMultipleTimes()
    {
        // Arrange
        var aggregator = CreateAggregator();
        var count = 0;
        Action<TestEvent> handler = _ => count++;

        aggregator.Subscribe(handler);
        aggregator.Subscribe(handler);

        // Act
        aggregator.Publish(new TestEvent());

        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public async Task ConcurrentPublish_ShouldDeliverEveryEvent()
    {
        var aggregator = CreateAggregator();
        var received = 0;
        Action<TestEvent> handler = _ => Interlocked.Increment(ref received);
        using var subscription = aggregator.Subscribe(handler);

        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ =>
            Task.Run(() => aggregator.Publish(new TestEvent()))));

        received.Should().Be(100);
    }

    [Fact]
    public async Task PublishAsync_WaitsForHandlersAndContinuesAfterAnException()
    {
        var aggregator = CreateAggregator();
        var received = new ConcurrentBag<string>();
        using var failing = aggregator.Subscribe<TestEvent>(_ => throw new InvalidOperationException());
        using var successful = aggregator.Subscribe<TestEvent>(value => received.Add(value.Message));

        await aggregator.PublishAsync(new TestEvent { Message = "published" });

        received.Should().ContainSingle().Which.Should().Be("published");
    }

    [Fact]
    public void CollectedSubscribersAreWeakAndDoNotPreventPublishing()
    {
        var aggregator = CreateAggregator();
        var receiver = SubscribeTemporaryReceiver(aggregator);

        var collected = false;
        for (var attempt = 0; attempt < 5 && !collected; attempt++)
            collected = IsCollected(receiver);

        collected.Should().BeTrue();
        aggregator.Publish(new TestEvent());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsCollected(WeakReference<Receiver> receiver)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return !receiver.TryGetTarget(out _);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Receiver> SubscribeTemporaryReceiver(IEventAggregator aggregator)
    {
        var receiver = new Receiver();
        aggregator.Subscribe<TestEvent>(receiver.Handle);
        return new WeakReference<Receiver>(receiver);
    }

    private sealed class Receiver
    {
        public void Handle(TestEvent value) { }
    }
}
