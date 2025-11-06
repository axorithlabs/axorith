using FluentAssertions;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Sdk.Tests.Actions;

public class ActionTests
{
    [Fact]
    public void Create_WithValidParameters_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var action = Action.Create("test-key", "Test Label", isEnabled: true);

        // Assert
        action.Should().NotBeNull();
        action.Key.Should().Be("test-key");
    }

    [Fact]
    public void Label_ShouldEmitInitialValue()
    {
        // Arrange
        var action = Action.Create("key", "Initial Label");
        string? emittedLabel = null;

        // Act
        action.Label.Subscribe(label => emittedLabel = label);

        // Assert
        emittedLabel.Should().Be("Initial Label");
    }

    [Fact]
    public void SetLabel_ShouldUpdateLabelObservable()
    {
        // Arrange
        var action = Action.Create("key", "Old Label");
        var emittedLabels = new List<string>();
        action.Label.Subscribe(label => emittedLabels.Add(label));

        // Act
        action.SetLabel("New Label");

        // Assert
        emittedLabels.Should().HaveCount(2);
        emittedLabels[0].Should().Be("Old Label");
        emittedLabels[1].Should().Be("New Label");
    }

    [Fact]
    public void IsEnabled_ShouldEmitInitialValue()
    {
        // Arrange
        var enabledAction = Action.Create("key", "Label", isEnabled: true);
        var disabledAction = Action.Create("key2", "Label2", isEnabled: false);
        bool? enabledValue = null;
        bool? disabledValue = null;

        // Act
        enabledAction.IsEnabled.Subscribe(value => enabledValue = value);
        disabledAction.IsEnabled.Subscribe(value => disabledValue = value);

        // Assert
        enabledValue.Should().BeTrue();
        disabledValue.Should().BeFalse();
    }

    [Fact]
    public void SetEnabled_ShouldUpdateIsEnabledObservable()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var emittedStates = new List<bool>();
        action.IsEnabled.Subscribe(state => emittedStates.Add(state));

        // Act
        action.SetEnabled(false);
        action.SetEnabled(true);

        // Assert
        emittedStates.Should().Equal(true, false, true);
    }

    [Fact]
    public void Invoke_WhenEnabled_ShouldEmitInvokedSignal()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var invokedCount = 0;
        action.Invoked.Subscribe(_ => invokedCount++);

        // Act
        action.Invoke();
        action.Invoke();

        // Assert
        invokedCount.Should().Be(2);
    }

    [Fact]
    public void Invoke_WhenDisabled_ShouldNotEmitInvokedSignal()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: false);
        var invokedCount = 0;
        action.Invoked.Subscribe(_ => invokedCount++);

        // Act
        action.Invoke();

        // Assert
        invokedCount.Should().Be(0);
    }

    [Fact]
    public void Invoke_AfterDisabling_ShouldNotEmitSignal()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: true);
        var invokedCount = 0;
        action.Invoked.Subscribe(_ => invokedCount++);

        // Act
        action.Invoke(); // Should work
        action.SetEnabled(false);
        action.Invoke(); // Should not work

        // Assert
        invokedCount.Should().Be(1);
    }

    [Fact]
    public void Invoke_AfterReEnabling_ShouldEmitSignalAgain()
    {
        // Arrange
        var action = Action.Create("key", "Label", isEnabled: false);
        var invokedCount = 0;
        action.Invoked.Subscribe(_ => invokedCount++);

        // Act
        action.Invoke(); // Should not work
        action.SetEnabled(true);
        action.Invoke(); // Should work

        // Assert
        invokedCount.Should().Be(1);
    }

    [Fact]
    public void MultipleSubscribers_ShouldAllReceiveUpdates()
    {
        // Arrange
        var action = Action.Create("key", "Label");
        var subscriber1Labels = new List<string>();
        var subscriber2Labels = new List<string>();

        action.Label.Subscribe(l => subscriber1Labels.Add(l));
        action.Label.Subscribe(l => subscriber2Labels.Add(l));

        // Act
        action.SetLabel("Updated");

        // Assert
        subscriber1Labels.Should().Equal("Label", "Updated");
        subscriber2Labels.Should().Equal("Label", "Updated");
    }

    [Theory]
    [InlineData("login", "Login", true)]
    [InlineData("logout", "Logout", false)]
    [InlineData("refresh", "Refresh Data", true)]
    public void Create_WithVariousParameters_ShouldWorkCorrectly(string key, string label, bool isEnabled)
    {
        // Act
        var action = Action.Create(key, label, isEnabled);

        // Assert
        action.Key.Should().Be(key);

        string? emittedLabel = null;
        bool? emittedEnabled = null;
        action.Label.Subscribe(l => emittedLabel = l);
        action.IsEnabled.Subscribe(e => emittedEnabled = e);

        emittedLabel.Should().Be(label);
        emittedEnabled.Should().Be(isEnabled);
    }
}