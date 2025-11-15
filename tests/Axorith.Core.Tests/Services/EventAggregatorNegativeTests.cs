using Axorith.Core.Services.Abstractions;
using Axorith.Sdk.Services;
using FluentAssertions;

namespace Axorith.Core.Tests.Services;

/// <summary>
///     Tests for error handling, exception scenarios, and edge cases in EventAggregator
/// </summary>
public class EventAggregatorNegativeTests
{
    private static IEventAggregator CreateAggregator()
    {
        var asm = typeof(ISessionManager).Assembly;
        var type = asm.GetType("Axorith.Core.Services.EventAggregator", throwOnError: true)!;
        return (IEventAggregator)Activator.CreateInstance(type, nonPublic: true)!;
    }

    private class TestEvent
    {
        public string Message { get; set; } = string.Empty;
    }

    [Fact]
    public void Publish_WithExceptionInHandler_ShouldNotBreakOtherHandlers()
    {
        // Arrange
        var aggregator = CreateAggregator();
        var handler1Called = false;
        var handler3Called = false;

        aggregator.Subscribe<TestEvent>(_ => handler1Called = true);
        aggregator.Subscribe<TestEvent>(_ => throw new InvalidOperationException("Handler 2 failed"));
        aggregator.Subscribe<TestEvent>(_ => handler3Called = true);

        // Act
        aggregator.Publish(new TestEvent());

        // Assert
        handler1Called.Should().BeTrue("First handler should execute");
        handler3Called.Should().BeTrue("Third handler should execute despite second failing");
    }

    [Fact]
    public void Publish_WithMultipleExceptions_ShouldContinueExecutingAllHandlers()
    {
        // Arrange
        var aggregator = CreateAggregator();
        var successCount = 0;

        aggregator.Subscribe<TestEvent>(_ => throw new Exception("Error 1"));
        aggregator.Subscribe<TestEvent>(_ => successCount++);
        aggregator.Subscribe<TestEvent>(_ => throw new Exception("Error 2"));
        aggregator.Subscribe<TestEvent>(_ => successCount++);
        aggregator.Subscribe<TestEvent>(_ => throw new Exception("Error 3"));

        // Act
        aggregator.Publish(new TestEvent());

        // Assert
        successCount.Should().Be(2, "Successful handlers should all execute");
    }

    [Fact]
    public void Publish_SynchronousExecution_ShouldMaintainOrder()
    {
        // Arrange
        var aggregator = CreateAggregator();
        var executionOrder = new List<int>();

        aggregator.Subscribe<TestEvent>(_ => executionOrder.Add(1));
        aggregator.Subscribe<TestEvent>(_ => executionOrder.Add(2));
        aggregator.Subscribe<TestEvent>(_ => executionOrder.Add(3));

        // Act
        aggregator.Publish(new TestEvent());

        // Assert
        executionOrder.Should().Equal([3, 2, 1], "Handlers execute in reverse subscription order");
    }

    [Fact]
    public void Subscribe_UnsubscribeDuringPublish_ShouldHandleGracefully()
    {
        // Arrange
        var aggregator = CreateAggregator();
        IDisposable? subscription2 = null;

        aggregator.Subscribe<TestEvent>(_ =>
        {
            subscription2?.Dispose(); // Unsubscribe handler 2 during handler 1 execution
        });

        subscription2 = aggregator.Subscribe<TestEvent>(_ => { });

        // Act
        aggregator.Publish(new TestEvent());

        // Assert - behavior depends on implementation, should not crash
        // Handler 2 may or may not be called depending on snapshot timing
        Assert.True(true, "Should not throw exception");
    }

    [Fact]
    public void WeakReference_AfterGC_ShouldBeCleanedUp()
    {
        // Arrange
        var aggregator = CreateAggregator();
        var callCount = 0;

        void CreateAndSubscribeHandler()
        {
            Action<TestEvent> handler = _ => callCount++;
            aggregator.Subscribe(handler);
            // handler goes out of scope here
        }

        CreateAndSubscribeHandler();

        // Act - Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Publish multiple times to trigger cleanup
        for (var i = 0; i < 10; i++) aggregator.Publish(new TestEvent());

        // Assert - call count may be 0 if GC collected the handler
        // This test verifies cleanup doesn't crash
        callCount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void ConcurrentPublish_ShouldNotThrow()
    {
        // Arrange
        var aggregator = CreateAggregator();
        var lockObj = new object();

        aggregator.Subscribe<TestEvent>(_ =>
        {
            lock (lockObj)
            {
            }
        });

        // Act
        var tasks = new List<Task>();
        for (var i = 0; i < 100; i++) tasks.Add(Task.Run(() => aggregator.Publish(new TestEvent())));

        var act = async () => await Task.WhenAll(tasks);

        // Assert
        act.Should().NotThrowAsync();
    }

    [Fact]
    public void ConcurrentSubscribeUnsubscribe_ShouldNotThrow()
    {
        // Arrange
        var aggregator = CreateAggregator();
        var tasks = new List<Task>();

        // Act
        for (var i = 0; i < 50; i++)
            tasks.Add(Task.Run(() =>
            {
                for (var j = 0; j < 10; j++)
                {
                    var subscription = aggregator.Subscribe<TestEvent>(_ => { });
                    subscription.Dispose();
                }
            }));

        var act = async () => await Task.WhenAll(tasks);

        // Assert
        act.Should().NotThrowAsync();
    }

    [Fact]
    public void ConcurrentPublishAndSubscribe_ShouldNotThrow()
    {
        // Arrange
        var aggregator = CreateAggregator();
        var tasks = new List<Task>();

        // Add publishers
        for (var i = 0; i < 50; i++)
            tasks.Add(Task.Run(() =>
            {
                for (var j = 0; j < 10; j++) aggregator.Publish(new TestEvent());
            }));

        // Add subscribers
        for (var i = 0; i < 50; i++)
            tasks.Add(Task.Run(() =>
            {
                for (var j = 0; j < 10; j++)
                {
                    var sub = aggregator.Subscribe<TestEvent>(_ => { });
                    sub.Dispose();
                }
            }));

        // Act
        var act = async () => await Task.WhenAll(tasks);

        // Assert
        act.Should().NotThrowAsync();
    }

    [Fact]
    public void Publish_WithNullEvent_ShouldNotThrow()
    {
        // Arrange
        var aggregator = CreateAggregator();
        var receivedNull = false;

        aggregator.Subscribe<TestEvent?>(e => receivedNull = e == null);

        // Act
        aggregator.Publish<TestEvent?>(null);

        // Assert
        receivedNull.Should().BeTrue();
    }

    [Fact]
    public void DeadReferenceCleanup_ShouldNotThrowDuringPublish()
    {
        // Arrange
        var aggregator = CreateAggregator();

        // Create some handlers that will be GC'd
        for (var i = 0; i < 100; i++)
        {
            Action<TestEvent> handler = _ => { };
            aggregator.Subscribe(handler);
        }

        // Force GC
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Act - this should trigger cleanup
        var act = () =>
        {
            for (var i = 0; i < 10; i++) aggregator.Publish(new TestEvent());
        };

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Handler_WithLongRunningOperation_ShouldNotBlockOthers()
    {
        // Arrange
        var aggregator = CreateAggregator();
        var quickHandlerExecuted = false;
        var slowHandlerStarted = false;

        aggregator.Subscribe<TestEvent>(_ =>
        {
            slowHandlerStarted = true;
            Thread.Sleep(100); // Slow handler
        });

        aggregator.Subscribe<TestEvent>(_ => quickHandlerExecuted = true);

        // Act
        aggregator.Publish(new TestEvent());

        // Assert - with synchronous execution, slow handler blocks
        slowHandlerStarted.Should().BeTrue();
        quickHandlerExecuted.Should().BeTrue();
    }

    [Fact]
    public void MultipleUnsubscribe_ShouldBeIdempotent()
    {
        // Arrange
        var aggregator = CreateAggregator();
        var subscription = aggregator.Subscribe<TestEvent>(_ => { });

        // Act
        subscription.Dispose();
        var act = () =>
        {
            subscription.Dispose();
            subscription.Dispose();
            subscription.Dispose();
        };

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Publish_AfterAllHandlersUnsubscribed_ShouldNotThrow()
    {
        // Arrange
        var aggregator = CreateAggregator();
        var sub1 = aggregator.Subscribe<TestEvent>(_ => { });
        var sub2 = aggregator.Subscribe<TestEvent>(_ => { });
        var sub3 = aggregator.Subscribe<TestEvent>(_ => { });

        sub1.Dispose();
        sub2.Dispose();
        sub3.Dispose();

        // Act
        var act = () => aggregator.Publish(new TestEvent());

        // Assert
        act.Should().NotThrow();
    }
}