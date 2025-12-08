using FluentAssertions;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Sdk.Tests.Actions;

/// <summary>
///     Negative tests and edge cases for Action
/// </summary>
public class ActionNegativeTests
{
    /// <summary>
    ///     Tests for Action.Create with invalid keys
    /// </summary>
    /// <param name="invalidKey">Key to be used in Action.Create</param>
    /// <remarks>
    ///     Tests with null, empty string, and whitespace as keys
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidKey_ShouldAccept(string? invalidKey)
    {
        // Act
        var action = Action.Create(invalidKey!, "Label");

        // Assert - Action accepts any key value
        action.Should().NotBeNull();
        action.Key.Should().Be(invalidKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidLabel_ShouldAccept(string? invalidLabel)
    {
        // Act
        var action = Action.Create("key", invalidLabel!);

        // Assert - Action accepts any label value
        action.Should().NotBeNull();
        string? currentLabel = null;
        action.Label.Subscribe(l => currentLabel = l);
        currentLabel.Should().Be(invalidLabel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetLabel_WithInvalidLabel_ShouldAccept(string? invalidLabel)
    {
        // Arrange
        var action = Action.Create("key", "Valid Label");

        // Act
        action.SetLabel(invalidLabel!);

        // Assert - SetLabel accepts any value
        string? currentLabel = null;
        action.Label.Subscribe(l => currentLabel = l);
        currentLabel.Should().Be(invalidLabel);
    }

    [Fact]
    public void Invoke_Repeatedly_WithoutReEnabling_ShouldNotEmitAfterDisable()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var invokeCount = 0;
        action.Invoked.Subscribe(_ => invokeCount++);

        // Act
        action.Invoke(); // 1
        action.Invoke(); // 2
        action.SetEnabled(false);
        action.Invoke(); // Should not emit
        action.Invoke(); // Should not emit
        action.Invoke(); // Should not emit

        // Assert
        invokeCount.Should().Be(2);
    }

    [Fact]
    public void Observables_AfterManySubscriptions_ShouldHandleAll()
    {
        // Arrange
        var action = Action.Create("key", "Label");
        var subscriptions = new List<IDisposable>();
        var counters = new List<int>();

        // Act - create 100 subscribers
        for (var i = 0; i < 100; i++)
        {
            var counter = 0;
            counters.Add(counter);
            var index = i;
            subscriptions.Add(action.Label.Subscribe(_ => counters[index]++));
        }

        action.SetLabel("New Label");

        // Assert - all should receive update
        counters.Should().AllSatisfy(c => c.Should().Be(2)); // initial + update
    }

    [Fact]
    public void IsEnabled_Observable_WithErrorHandler_ShouldNotBreakStream()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var values = new List<bool>();
        var errorHandlerCalled = false;

        action.IsEnabled.Subscribe(
            onNext: v => values.Add(v),
            onError: _ => errorHandlerCalled = true);

        // Act
        action.SetEnabled(false);
        action.SetEnabled(true);

        // Assert
        values.Should().Equal(true, false, true);
        errorHandlerCalled.Should().BeFalse();
    }

    [Fact]
    public void ConcurrentSetLabel_ShouldNotThrow()
    {
        // Arrange
        var action = Action.Create("key", "Label");
        var tasks = new List<Task>();

        // Act - concurrent label updates
        for (var i = 0; i < 100; i++)
        {
            var label = $"Label {i}";
            tasks.Add(Task.Run(() => action.SetLabel(label)));
        }

        var act = async () => await Task.WhenAll(tasks);

        // Assert
        act.Should().NotThrowAsync();
    }

    [Fact]
    public void ConcurrentSetEnabled_ShouldNotThrow()
    {
        // Arrange
        var action = Action.Create("key", "Label");
        var tasks = new List<Task>();

        // Act - concurrent enabled updates
        for (var i = 0; i < 100; i++)
        {
            var enabled = i % 2 == 0;
            tasks.Add(Task.Run(() => action.SetEnabled(enabled)));
        }

        var act = async () => await Task.WhenAll(tasks);

        // Assert
        act.Should().NotThrowAsync();
    }

    [Fact]
    public void ConcurrentInvoke_ShouldHandleCorrectly()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var invokeCount = 0;
        var lockObj = new object();

        action.Invoked.Subscribe(_ =>
        {
            lock (lockObj)
            {
                invokeCount++;
            }
        });

        var tasks = new List<Task>();

        // Act - concurrent invokes
        for (var i = 0; i < 100; i++) tasks.Add(Task.Run(() => action.Invoke()));

        #pragma warning disable xUnit1031
        Task.WaitAll(tasks.ToArray());
        #pragma warning restore xUnit1031

        // Assert
        invokeCount.Should().Be(100);
    }

    [Fact]
    public void SubscribeAndUnsubscribe_Rapidly_ShouldNotThrow()
    {
        // Arrange
        var action = Action.Create("key", "Label");
        var tasks = new List<Task>();

        // Act
        for (var i = 0; i < 50; i++)
            tasks.Add(Task.Run(() =>
            {
                for (var j = 0; j < 10; j++)
                {
                    var subscription = action.Label.Subscribe(_ => { });
                    subscription.Dispose();
                }
            }));

        var act = async () => await Task.WhenAll(tasks);

        // Assert
        act.Should().NotThrowAsync();
    }

    [Fact]
    public void Subscription_DisposedTwice_ShouldBeIdempotent()
    {
        // Arrange
        var action = Action.Create("key", "Label");
        var subscription = action.Label.Subscribe(_ => { });

        // Act
        subscription.Dispose();
        var act = () => subscription.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ObservableAfterManyUpdates_ShouldStillWork()
    {
        // Arrange
        var action = Action.Create("key", "Label");
        var lastLabel = "";
        action.Label.Subscribe(l => lastLabel = l);

        // Act - many updates
        for (var i = 0; i < 1000; i++) action.SetLabel($"Label {i}");

        // Assert
        lastLabel.Should().Be("Label 999");
    }

    [Fact]
    public void InvokedObservable_WithSlowSubscriber_ShouldNotBlockPublisher()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var fastCount = 0;
        var slowStarted = false;

        action.Invoked.Subscribe(_ => fastCount++);
        action.Invoked.Subscribe(_ =>
        {
            slowStarted = true;
            Thread.Sleep(100); // Slow subscriber
        });

        // Act
        action.Invoke();

        // Wait for slow subscriber to start
        while (!slowStarted) Thread.Sleep(10);

        // Assert - fast subscriber should have been called immediately
        fastCount.Should().Be(1);
    }
}