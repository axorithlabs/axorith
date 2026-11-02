using Axorith.Sdk.Settings;
using FluentAssertions;

namespace Axorith.Sdk.Tests.Settings;

/// <summary>
///     Tests for subscription lifetime and exact delivery of setting values
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

        intValues.Should().Equal(new object?[] { 0 }.Concat(Enumerable.Range(0, 100).Cast<object?>()));
        boolValues.Should().Equal(new object?[] { false }.Concat(Enumerable.Range(0, 100).Select(i => (object?)(i % 2 == 0))));
        textValues.Should().Equal(new object?[] { "" }.Concat(Enumerable.Range(0, 100).Select(i => (object?)$"Value{i}")));
    }
}
