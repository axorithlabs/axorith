using FluentAssertions;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Sdk.Tests.Actions;

public class ActionTests
{
    [Fact]
    public void Label_ShouldEmitInitialValue()
    {
        var action = new Action("key", "Initial Label");
        string? emittedLabel = null;

        action.Label.Subscribe(label => emittedLabel = label);

        emittedLabel.Should().Be("Initial Label");
    }

    [Fact]
    public void SettingKeyOverloadAssociatesActionWithItsSetting()
    {
        var action = new Action("key", "Label", "setting-key");

        action.SettingKey.Should().Be("setting-key");
    }

    [Fact]
    public void SetLabel_ShouldUpdateLabelObservable()
    {
        var action = new Action("key", "Old Label");
        var emittedLabels = new List<string>();
        action.Label.Subscribe(label => emittedLabels.Add(label));

        action.SetLabel("New Label");

        emittedLabels.Should().HaveCount(2);
        emittedLabels[0].Should().Be("Old Label");
        emittedLabels[1].Should().Be("New Label");
    }

    [Fact]
    public void IsEnabled_ShouldEmitInitialValue()
    {
        var enabledAction = new Action("key", "Label", isEnabled: true);
        var disabledAction = new Action("key2", "Label2", isEnabled: false);
        bool? enabledValue = null;
        bool? disabledValue = null;

        enabledAction.IsEnabled.Subscribe(value => enabledValue = value);
        disabledAction.IsEnabled.Subscribe(value => disabledValue = value);

        enabledValue.Should().BeTrue();
        disabledValue.Should().BeFalse();
    }

    [Fact]
    public void SetEnabled_ShouldUpdateIsEnabledObservable()
    {
        var action = new Action("key", "Label", isEnabled: true);
        var emittedStates = new List<bool>();
        action.IsEnabled.Subscribe(state => emittedStates.Add(state));

        action.SetEnabled(false);
        action.SetEnabled(true);

        emittedStates.Should().Equal(true, false, true);
    }

    [Fact]
    public void Invoke_WhenEnabled_ShouldEmitInvokedSignal()
    {
        var action = new Action("key", "Label", isEnabled: true);
        var invokedCount = 0;
        action.Invoked.Subscribe(_ => invokedCount++);

        action.Invoke();
        action.Invoke();

        invokedCount.Should().Be(2);
    }

    [Fact]
    public void Invoke_WhenDisabled_ShouldNotEmitInvokedSignal()
    {
        var action = new Action("key", "Label", isEnabled: false);
        var invokedCount = 0;
        action.Invoked.Subscribe(_ => invokedCount++);

        action.Invoke();

        invokedCount.Should().Be(0);
    }

    [Fact]
    public void Invoke_RespectsEnabledStateTransitions()
    {
        var action = new Action("key", "Label", isEnabled: false);
        var invokedCount = 0;
        action.Invoked.Subscribe(_ => invokedCount++);

        action.Invoke(); // Initially disabled
        action.SetEnabled(true);
        action.Invoke();
        action.SetEnabled(false);
        action.Invoke();

        invokedCount.Should().Be(1);
    }

    [Fact]
    public void MultipleSubscribers_ShouldAllReceiveUpdates()
    {
        var action = new Action("key", "Label");
        var subscriber1Labels = new List<string>();
        var subscriber2Labels = new List<string>();

        action.Label.Subscribe(l => subscriber1Labels.Add(l));
        action.Label.Subscribe(l => subscriber2Labels.Add(l));

        action.SetLabel("Updated");

        subscriber1Labels.Should().Equal("Label", "Updated");
        subscriber2Labels.Should().Equal("Label", "Updated");
    }

}
