using System.Reactive.Linq;
using Axorith.Sdk.Settings;
using FluentAssertions;

namespace Axorith.Sdk.Tests.Settings;

/// <summary>
///     Tests for reactive behavior of settings
/// </summary>
public class SettingReactivityTests
{
    [Fact]
    public void Value_WhenChanged_ShouldNotifyAllSubscribers()
    {
        // Arrange
        var setting = Setting.AsInt("count", "Count", 0);
        var subscriber1Values = new List<int>();
        var subscriber2Values = new List<int>();

        setting.Value.Subscribe(v => subscriber1Values.Add(v));
        setting.Value.Subscribe(v => subscriber2Values.Add(v));

        // Act
        setting.SetValue(1);
        setting.SetValue(2);
        setting.SetValue(3);

        // Assert
        subscriber1Values.Should().Equal(0, 1, 2, 3);
        subscriber2Values.Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void Value_WithFilter_ShouldOnlyEmitMatchingValues()
    {
        // Arrange
        var setting = Setting.AsInt("count", "Count", 0);
        var evenValues = new List<int>();

        setting.Value
            .Where(v => v % 2 == 0)
            .Subscribe(v => evenValues.Add(v));

        // Act
        setting.SetValue(1);
        setting.SetValue(2);
        setting.SetValue(3);
        setting.SetValue(4);

        // Assert
        evenValues.Should().Equal(0, 2, 4);
    }

    [Fact]
    public void Value_WithDistinctUntilChanged_ShouldSkipDuplicates()
    {
        // Arrange
        var setting = Setting.AsText("name", "Name", "initial");
        var distinctValues = new List<string>();

        setting.Value
            .DistinctUntilChanged()
            .Subscribe(v => distinctValues.Add(v));

        // Act
        setting.SetValue("A");
        setting.SetValue("A"); // Duplicate
        setting.SetValue("B");
        setting.SetValue("B"); // Duplicate
        setting.SetValue("A"); // Changed

        // Assert
        distinctValues.Should().Equal("initial", "A", "B", "A");
    }

    [Fact]
    public void Label_WhenChanged_ShouldNotifySubscribers()
    {
        // Arrange
        var setting = Setting.AsText("key", "Initial Label", "value");
        var labels = new List<string>();

        setting.Label.Subscribe(l => labels.Add(l));

        // Act
        setting.SetLabel("Updated Label");
        setting.SetLabel("Final Label");

        // Assert
        labels.Should().Equal("Initial Label", "Updated Label", "Final Label");
    }

    [Fact]
    public void IsVisible_WhenToggled_ShouldNotifySubscribers()
    {
        // Arrange
        var setting = Setting.AsText("key", "Label", "value", isVisible: true);
        var visibilities = new List<bool>();

        setting.IsVisible.Subscribe(v => visibilities.Add(v));

        // Act
        setting.SetVisibility(false);
        setting.SetVisibility(true);
        setting.SetVisibility(false);

        // Assert
        visibilities.Should().Equal(true, false, true, false);
    }

    [Fact]
    public void IsReadOnly_WhenToggled_ShouldNotifySubscribers()
    {
        // Arrange
        var setting = Setting.AsText("key", "Label", "value", isReadOnly: false);
        var readOnlyStates = new List<bool>();

        setting.IsReadOnly.Subscribe(r => readOnlyStates.Add(r));

        // Act
        setting.SetReadOnly(true);
        setting.SetReadOnly(false);

        // Assert
        readOnlyStates.Should().Equal(false, true, false);
    }

    [Fact]
    public void Choices_WhenUpdated_ShouldNotifySubscribers()
    {
        // Arrange
        var initialChoices = new List<KeyValuePair<string, string>>
        {
            new("a", "A")
        };
        var setting = Setting.AsChoice("select", "Select", "a", initialChoices);
        var choiceUpdates = new List<IReadOnlyList<KeyValuePair<string, string>>>();

        setting.Choices!.Subscribe(c => choiceUpdates.Add(c));

        // Act
        var newChoices = new List<KeyValuePair<string, string>>
        {
            new("a", "A"),
            new("b", "B")
        };
        setting.SetChoices(newChoices);

        // Assert
        choiceUpdates.Should().HaveCount(2);
        choiceUpdates[0].Should().HaveCount(1);
        choiceUpdates[1].Should().HaveCount(2);
    }

    [Fact]
    public void ValueAsObject_ShouldEmitWhenValueChanges()
    {
        // Arrange
        var setting = Setting.AsCheckbox("check", "Check", false);
        var objectValues = new List<object?>();

        setting.ValueAsObject.Subscribe(v => objectValues.Add(v));

        // Act
        setting.SetValue(true);
        setting.SetValue(false);

        // Assert
        objectValues.Should().HaveCount(3);
        objectValues[0].Should().Be(false);
        objectValues[1].Should().Be(true);
        objectValues[2].Should().Be(false);
    }

    [Fact]
    public void MultipleObservables_ShouldAllEmitOnChanges()
    {
        // Arrange
        var setting = Setting.AsText("key", "Label", "initial");
        var valueEmissions = 0;
        var labelEmissions = 0;
        var visibilityEmissions = 0;

        setting.Value.Subscribe(_ => valueEmissions++);
        setting.Label.Subscribe(_ => labelEmissions++);
        setting.IsVisible.Subscribe(_ => visibilityEmissions++);

        // Act
        setting.SetValue("new value");
        setting.SetLabel("new label");
        setting.SetVisibility(false);

        // Assert
        valueEmissions.Should().Be(2); // initial + 1 change
        labelEmissions.Should().Be(2); // initial + 1 change
        visibilityEmissions.Should().Be(2); // initial + 1 change
    }

    [Fact]
    public async Task Value_WithThrottle_ShouldDebounceRapidChanges()
    {
        // Arrange
        var setting = Setting.AsInt("counter", "Counter", 0);
        var throttledValues = new List<int>();

        setting.Value
            .Throttle(TimeSpan.FromMilliseconds(50))
            .Subscribe(v => throttledValues.Add(v));

        // Act
        setting.SetValue(1);
        setting.SetValue(2);
        setting.SetValue(3);
        await Task.Delay(100); // Wait for throttle

        // Assert
        // Should only get final value after throttle
        throttledValues.Should().Contain(3);
        throttledValues.Should().NotBeEmpty();
    }

    [Fact]
    public void Subscription_WhenDisposed_ShouldStopReceivingUpdates()
    {
        // Arrange
        var setting = Setting.AsInt("num", "Number", 0);
        var values = new List<int>();
        var subscription = setting.Value.Subscribe(v => values.Add(v));

        // Act
        setting.SetValue(1); // Should receive
        subscription.Dispose();
        setting.SetValue(2); // Should not receive

        // Assert
        values.Should().Equal(0, 1);
        values.Should().NotContain(2);
    }

    [Fact]
    public void CombineLatest_WithMultipleSettings_ShouldEmitWhenAnyChanges()
    {
        // Arrange
        var firstName = Setting.AsText("first", "First", "");
        var lastName = Setting.AsText("last", "Last", "");
        var fullNames = new List<string>();

        firstName.Value.CombineLatest(lastName.Value,
                (f, l) => $"{f} {l}".Trim())
            .Subscribe(full => fullNames.Add(full));

        // Act
        firstName.SetValue("John");
        lastName.SetValue("Doe");

        // Assert
        fullNames.Should().Contain("John");
        fullNames.Should().Contain("John Doe");
    }
}