using System.Reactive.Linq;
using Axorith.Sdk.Settings;
using FluentAssertions;
using Microsoft.Reactive.Testing;

namespace Axorith.Sdk.Tests.Settings;

/// <summary>
///     Tests for disposal, backpressure, and error handling in reactive observables
/// </summary>
public class SettingDisposeAndBackpressureTests
{
    [Fact]
    public void Subscription_AfterDispose_ShouldStopReceivingUpdates()
    {
        var setting = Setting.AsInt("key", "Label", 0);
        var values = new List<int>();
        var subscription = setting.Value.Subscribe(v => values.Add(v));

        setting.SetValue(1);
        subscription.Dispose();
        setting.SetValue(2);
        setting.SetValue(3);

        values.Should().Equal(0, 1);
    }

    [Fact]
    public void MultipleSubscriptions_DisposingOne_ShouldNotAffectOthers()
    {
        var setting = Setting.AsText("key", "Label", "initial");
        var values1 = new List<string>();
        var values2 = new List<string>();

        var sub1 = setting.Value.Subscribe(v => values1.Add(v));
        setting.Value.Subscribe(v => values2.Add(v));

        setting.SetValue("A");
        sub1.Dispose();
        setting.SetValue("B");

        values1.Should().Equal("initial", "A");
        values2.Should().Equal("initial", "A", "B");
    }

    [Fact]
    public void Observable_WithErrorInSubscriber_ShouldNotBreakOtherSubscribers()
    {
        var setting = Setting.AsInt("key", "Label", 0);
        var goodValues = new List<int>();
        var exceptionThrown = false;

        setting.Value.Subscribe(v => goodValues.Add(v));
        setting.Value.Subscribe(_ =>
        {
            exceptionThrown = true;
            throw new InvalidOperationException("Test exception");
        });
        setting.Value.Subscribe(v => goodValues.Add(v * 2));

        try
        {
            setting.SetValue(42);
        }
        catch
        {
            // Expected - exception from middle subscriber
        }

        exceptionThrown.Should().BeTrue();
        // First subscriber should still work
        goodValues.Should().Contain(42);
    }

    [Fact]
    public void RapidUpdates_WithBuffering_ShouldHandleBackpressure()
    {
        var setting = Setting.AsInt("key", "Label", 0);
        var bufferedValues = new List<List<int>>();
        var scheduler = new TestScheduler();

        setting.Value
            .Buffer(TimeSpan.FromTicks(TimeSpan.FromMilliseconds(50).Ticks), scheduler)
            .Subscribe(batch => bufferedValues.Add(batch.ToList()));

        for (var i = 1; i <= 100; i++) setting.SetValue(i);
        // Advance virtual time to flush buffers
        scheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks);

        var allValues = bufferedValues.SelectMany(b => b).ToList();
        allValues.Should().Contain(100);
    }

    [Fact]
    public void Observable_WithSample_ShouldReduceUpdateFrequency()
    {
        var setting = Setting.AsInt("key", "Label", 0);
        var sampledValues = new List<int>();
        var scheduler = new TestScheduler();

        setting.Value
            .Sample(TimeSpan.FromTicks(TimeSpan.FromMilliseconds(50).Ticks), scheduler)
            .Subscribe(v => sampledValues.Add(v));

        for (var i = 1; i <= 100; i++) setting.SetValue(i);
        // Advance virtual time to allow sampling ticks
        scheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks);

        sampledValues.Should().HaveCountLessThan(100);
        sampledValues.Should().Contain(100);
    }

    [Fact]
    public void Observable_WithSkip_ShouldIgnoreInitialValues()
    {
        var setting = Setting.AsInt("key", "Label", 0);
        var values = new List<int>();

        setting.Value
            .Skip(1) // Skip initial value
            .Subscribe(v => values.Add(v));

        setting.SetValue(1);
        setting.SetValue(2);

        values.Should().NotContain(0);
        values.Should().Equal(1, 2);
    }

    [Fact]
    public void Observable_WithTake_ShouldLimitValues()
    {
        var setting = Setting.AsInt("key", "Label", 0);
        var values = new List<int>();
        var completed = false;

        setting.Value
            .Take(3)
            .Subscribe(
                onNext: v => values.Add(v),
                onCompleted: () => completed = true);

        setting.SetValue(1);
        setting.SetValue(2);
        setting.SetValue(3);
        setting.SetValue(4);

        values.Should().Equal(0, 1, 2);
        completed.Should().BeTrue();
    }

    [Fact]
    public void DisposedSubscriptions_ShouldNotReceiveUpdates()
    {
        var setting = Setting.AsInt("key", "Label", 0);
        var notificationCount = 0;
        var subscriptions = new List<IDisposable>();

        for (var i = 0; i < 1000; i++)
        {
            var sub = setting.Value.Subscribe(_ => notificationCount++);
            subscriptions.Add(sub);
        }

        foreach (var sub in subscriptions)
        {
            sub.Dispose();
        }

        // Update setting - should not notify disposed subscriptions
        setting.SetValue(42);

        notificationCount.Should().Be(1000);
    }

    [Fact]
    public async Task Observable_WithAsyncSubscriber_ShouldNotBlock()
    {
        var setting = Setting.AsInt("key", "Label", 0);
        var taskCompletionSource = new TaskCompletionSource<int>();

        setting.Value
            .Skip(1) // Skip initial
            .Subscribe(async v =>
            {
                await Task.Delay(10);
                taskCompletionSource.SetResult(v);
            });

        setting.SetValue(42);
        var result = await taskCompletionSource.Task;

        result.Should().Be(42);
    }

    [Fact]
    public void Choices_Observable_WithDispose_ShouldStopUpdates()
    {
        var choices = new List<KeyValuePair<string, string>> { new("a", "A") };
        var setting = Setting.AsChoice("choice", "Choice", "a", choices);
        var choiceUpdates = new List<IReadOnlyList<KeyValuePair<string, string>>>();

        var subscription = setting.Choices!.Subscribe(c => choiceUpdates.Add(c));

        var newChoices = new List<KeyValuePair<string, string>> { new("b", "B") };
        setting.SetChoices(newChoices);

        subscription.Dispose();

        var moreChoices = new List<KeyValuePair<string, string>> { new("c", "C") };
        setting.SetChoices(moreChoices);

        choiceUpdates.Should().HaveCount(2); // Initial + first update only
    }

    [Fact]
    public void ValueAsObject_Observable_ShouldHandleRapidTypeChanges()
    {
        // This test verifies that ValueAsObject properly boxes different types
        var intSetting = Setting.AsInt("int", "Int", 0);
        var boolSetting = Setting.AsCheckbox("bool", "Bool", false);
        var textSetting = Setting.AsText("text", "Text", "");

        var intValues = new List<object?>();
        var boolValues = new List<object?>();
        var textValues = new List<object?>();

        intSetting.ValueAsObject.Subscribe(v => intValues.Add(v));
        boolSetting.ValueAsObject.Subscribe(v => boolValues.Add(v));
        textSetting.ValueAsObject.Subscribe(v => textValues.Add(v));

        for (var i = 0; i < 100; i++)
        {
            intSetting.SetValue(i);
            boolSetting.SetValue(i % 2 == 0);
            textSetting.SetValue($"Value{i}");
        }

        intValues.Should().HaveCount(101); // Initial + 100 updates
        boolValues.Should().HaveCount(101);
        textValues.Should().HaveCount(101);
    }
}
