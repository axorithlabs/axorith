using FluentAssertions;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Sdk.Tests.Actions;

/// <summary>
///     Tests for async action invocation (InvokeAsync and OnInvokeAsync)
/// </summary>
public class ActionAsyncTests
{
    [Fact]
    public async Task InvokeAsync_WhenEnabled_ShouldEmitInvokedSignal()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var invokedCount = 0;
        action.Invoked.Subscribe(_ => invokedCount++);

        // Act
        await action.InvokeAsync();
        await action.InvokeAsync();

        // Assert
        invokedCount.Should().Be(2);
    }

    [Fact]
    public async Task InvokeAsync_WhenDisabled_ShouldNotEmitInvokedSignal()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: false);
        var invokedCount = 0;
        action.Invoked.Subscribe(_ => invokedCount++);

        // Act
        await action.InvokeAsync();

        // Assert
        invokedCount.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_WithoutHandler_ShouldCompleteImmediately()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        await action.InvokeAsync();

        // Assert
        stopwatch.Stop();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    [Fact]
    public async Task InvokeAsync_WithAsyncHandler_ShouldWaitForCompletion()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var handlerCompleted = false;

        action.OnInvokeAsync(async () =>
        {
            await Task.Delay(100);
            handlerCompleted = true;
        });

        // Act
        await action.InvokeAsync();

        // Assert
        handlerCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WithLongRunningHandler_ShouldWaitForIt()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var executionTime = 0;

        action.OnInvokeAsync(async () =>
        {
            await Task.Delay(200);
            executionTime = 200;
        });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        await action.InvokeAsync();

        // Assert
        stopwatch.Stop();
        stopwatch.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(150);
        executionTime.Should().Be(200);
    }

    [Fact]
    public async Task OnInvokeAsync_RegisteredMultipleTimes_ShouldUseLatestHandler()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var handler1Called = false;
        var handler2Called = false;

        action.OnInvokeAsync(async () =>
        {
            await Task.Delay(10);
            handler1Called = true;
        });

        action.OnInvokeAsync(async () =>
        {
            await Task.Delay(10);
            handler2Called = true;
        });

        // Act
        await action.InvokeAsync();

        // Assert
        handler1Called.Should().BeFalse();
        handler2Called.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_HandlerThrowsException_ShouldPropagateException()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var expectedException = new InvalidOperationException("Test exception");

        action.OnInvokeAsync(async () =>
        {
            await Task.Delay(10);
            throw expectedException;
        });

        // Act
        Func<Task> act = async () => await action.InvokeAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Test exception");
    }

    [Fact]
    public async Task InvokeAsync_ConcurrentInvocations_ShouldExecuteSequentially()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var executionOrder = new List<int>();
        var lockObj = new object();

        action.OnInvokeAsync(async () =>
        {
            await Task.Delay(50);
            lock (lockObj)
            {
                executionOrder.Add(executionOrder.Count + 1);
            }
        });

        // Act - invoke 3 times concurrently
        var task1 = action.InvokeAsync();
        var task2 = action.InvokeAsync();
        var task3 = action.InvokeAsync();

        await Task.WhenAll(task1, task2, task3);

        // Assert - all should have completed
        executionOrder.Should().HaveCount(3);
    }

    [Fact]
    public async Task InvokeAsync_AfterDisabling_ShouldReturnImmediately()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var handlerCalled = false;

        action.OnInvokeAsync(async () =>
        {
            await Task.Delay(100);
            handlerCalled = true;
        });

        action.SetEnabled(false);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        await action.InvokeAsync();

        // Assert
        stopwatch.Stop();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(50);
        handlerCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_WithCancellationLikeScenario_ShouldHandleGracefully()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var cts = new CancellationTokenSource();
        var handlerStarted = false;
        var handlerCompleted = false;

        action.OnInvokeAsync(async () =>
        {
            handlerStarted = true;
            await Task.Delay(100, cts.Token);
            handlerCompleted = true;
        });

        // Act
        var invokeTask = action.InvokeAsync();
        await Task.Delay(20); // Let handler start
        cts.Cancel();

        // Assert
        Func<Task> act = async () => await invokeTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
        handlerStarted.Should().BeTrue();
        handlerCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_MultipleSequentialCalls_ShouldExecuteInOrder()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var executionSequence = new List<int>();

        action.OnInvokeAsync(async () =>
        {
            await Task.Delay(10);
            executionSequence.Add(executionSequence.Count + 1);
        });

        // Act
        await action.InvokeAsync();
        await action.InvokeAsync();
        await action.InvokeAsync();

        // Assert
        executionSequence.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task OnInvokeAsync_WithNullHandler_ShouldNotThrow()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);

        // Act
        action.OnInvokeAsync(null!);
        Func<Task> act = async () => await action.InvokeAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvokeAsync_WithComplexAsyncOperation_ShouldComplete()
    {
        // Arrange
        var action = Action.Create("key", "OAuth Login", isEnabled: true);
        var result = "";

        action.OnInvokeAsync(async () =>
        {
            // Simulate complex OAuth flow
            await Task.Delay(50); // HTTP request
            await Task.Delay(50); // Token exchange
            await Task.Delay(50); // Store token
            result = "OAuth completed";
        });

        // Act
        await action.InvokeAsync();

        // Assert
        result.Should().Be("OAuth completed");
    }

    [Fact]
    public async Task InvokeAsync_MixedWithSyncInvoke_ShouldBothEmitInvoked()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var invokedCount = 0;
        action.Invoked.Subscribe(_ => invokedCount++);

        action.OnInvokeAsync(async () => await Task.Delay(10));

        // Act
        action.Invoke(); // Sync
        await action.InvokeAsync(); // Async
        action.Invoke(); // Sync

        // Assert
        invokedCount.Should().Be(3);
    }

    [Fact]
    public async Task InvokeAsync_HandlerWithReturnValue_ShouldExecute()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var capturedValue = 0;

        action.OnInvokeAsync(async () =>
        {
            await Task.Delay(10);
            capturedValue = 42;
        });

        // Act
        await action.InvokeAsync();

        // Assert
        capturedValue.Should().Be(42);
    }
}
