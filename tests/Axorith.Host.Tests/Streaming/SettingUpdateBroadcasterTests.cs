using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Streaming;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Action = Axorith.Sdk.Actions.Action;
using SdkSetting = Axorith.Sdk.Settings.Setting;

namespace Axorith.Host.Tests.Streaming;

public class SettingUpdateBroadcasterTests : IDisposable
{
    private readonly Mock<ISessionManager> _mockSessionManager;
    private readonly SettingUpdateBroadcaster _broadcaster;

    public SettingUpdateBroadcasterTests()
    {
        _mockSessionManager = new Mock<ISessionManager>();
        _broadcaster = new SettingUpdateBroadcaster(
            _mockSessionManager.Object,
            NullLogger<SettingUpdateBroadcaster>.Instance,
            Options.Create(new Configuration())
        );
    }

    public void Dispose()
    {
        _broadcaster.Dispose();
    }

    #region SubscribeAsync Tests

    [Fact]
    public async Task SubscribeAsync_WithValidParameters_ShouldAddSubscriber()
    {
        // Arrange
        var mockStream = new Mock<IServerStreamWriter<SettingUpdate>>();
        var cts = new CancellationTokenSource();

        // Act
        var subscribeTask = _broadcaster.SubscribeAsync("subscriber-1", null, mockStream.Object, cts.Token);
        await Task.Delay(50);

        await cts.CancelAsync();

        // Assert - should complete without error
        var completed = await Task.WhenAny(subscribeTask, Task.Delay(TimeSpan.FromSeconds(2))) == subscribeTask;
        completed.Should().BeTrue();
    }

    [Fact]
    public async Task SubscribeAsync_WithNullSubscriberId_ShouldThrow()
    {
        // Arrange
        var mockStream = new Mock<IServerStreamWriter<SettingUpdate>>();

        // Act
        var act = async () =>
            await _broadcaster.SubscribeAsync(null!, null, mockStream.Object, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SubscribeAsync_WithNullStream_ShouldThrow()
    {
        // Act
        var act = async () =>
            await _broadcaster.SubscribeAsync("subscriber-1", null, null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SubscribeAsync_WithModuleFilter_ShouldCreateFilteredSubscription()
    {
        // Arrange
        var mockStream = new Mock<IServerStreamWriter<SettingUpdate>>();
        var cts = new CancellationTokenSource();
        var moduleId = Guid.NewGuid().ToString();

        // Act
        var subscribeTask = _broadcaster.SubscribeAsync("subscriber-1", moduleId, mockStream.Object, cts.Token);
        await Task.Delay(50);

        await cts.CancelAsync();

        // Assert - should complete without error
        var completed = await Task.WhenAny(subscribeTask, Task.Delay(TimeSpan.FromSeconds(2))) == subscribeTask;
        completed.Should().BeTrue();
    }

    [Fact]
    public async Task SubscribeAsync_DuplicateSubscriber_ShouldReplace()
    {
        // Arrange
        var mockStream1 = new Mock<IServerStreamWriter<SettingUpdate>>();
        var mockStream2 = new Mock<IServerStreamWriter<SettingUpdate>>();
        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();

        // Act
        var sub1 = _broadcaster.SubscribeAsync("same-id", null, mockStream1.Object, cts1.Token);
        await Task.Delay(50);
        var sub2 = _broadcaster.SubscribeAsync("same-id", null, mockStream2.Object, cts2.Token);
        await Task.Delay(50);

        await cts1.CancelAsync();
        await cts2.CancelAsync();

        // Assert - both should complete
        await Task.WhenAll(sub1, sub2);
    }

    #endregion

    #region BroadcastUpdateAsync Tests

    [Fact]
    public async Task BroadcastUpdateAsync_WithNoSubscribers_ShouldNotThrow()
    {
        // Arrange
        var instanceId = Guid.NewGuid();

        // Act
        var act = async () => await _broadcaster.BroadcastUpdateAsync(
            instanceId, "settingKey", SettingProperty.Value, "newValue");

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task BroadcastUpdateAsync_ToSubscriber_ShouldSendUpdate()
    {
        // Arrange
        var receivedUpdates = new List<SettingUpdate>();
        var mockStream = CreateMockStream(receivedUpdates);
        var cts = new CancellationTokenSource();

        var subscribeTask = _broadcaster.SubscribeAsync("subscriber", null, mockStream.Object, cts.Token);
        await Task.Delay(50);

        var instanceId = Guid.NewGuid();

        // Act
        await _broadcaster.BroadcastUpdateAsync(instanceId, "settingKey", SettingProperty.Value, "newValue");
        await Task.Delay(100);

        await cts.CancelAsync();
        await subscribeTask;

        // Assert
        receivedUpdates.Should().ContainSingle();
        receivedUpdates[0].SettingKey.Should().Be("settingKey");
    }

    [Fact]
    public async Task BroadcastUpdateAsync_WithLabelProperty_ShouldIncludeLabel()
    {
        // Arrange
        var receivedUpdates = new List<SettingUpdate>();
        var mockStream = CreateMockStream(receivedUpdates);
        var cts = new CancellationTokenSource();

        var subscribeTask = _broadcaster.SubscribeAsync("subscriber", null, mockStream.Object, cts.Token);
        await Task.Delay(50);

        var instanceId = Guid.NewGuid();

        // Act
        await _broadcaster.BroadcastUpdateAsync(instanceId, "key", SettingProperty.Label, "New Label");
        await Task.Delay(100);

        await cts.CancelAsync();
        await subscribeTask;

        // Assert
        receivedUpdates.Should().ContainSingle();
        receivedUpdates[0].Property.Should().Be(SettingProperty.Label);
        receivedUpdates[0].StringValue.Should().Be("New Label");
    }

    [Fact]
    public async Task BroadcastUpdateAsync_ToFilteredSubscriber_ShouldOnlyReceiveMatchingUpdates()
    {
        // Arrange
        var receivedUpdates = new List<SettingUpdate>();
        var mockStream = CreateMockStream(receivedUpdates);
        var cts = new CancellationTokenSource();

        var targetInstanceId = Guid.NewGuid();
        var otherInstanceId = Guid.NewGuid();

        var subscribeTask = _broadcaster.SubscribeAsync(
            "subscriber", targetInstanceId.ToString(), mockStream.Object, cts.Token);
        await Task.Delay(50);

        // Act - send to both instances
        await _broadcaster.BroadcastUpdateAsync(targetInstanceId, "key", SettingProperty.Value, "target");
        await _broadcaster.BroadcastUpdateAsync(otherInstanceId, "key", SettingProperty.Value, "other");
        await Task.Delay(100);

        await cts.CancelAsync();
        await subscribeTask;

        // Assert - should only receive one update (for the filtered instance)
        receivedUpdates.Should().ContainSingle(u => u.StringValue == "target");
    }

    #endregion

    #region SubscribeToSetting Tests

    [Fact]
    public async Task SubscribeToSetting_ShouldBroadcastOnValueChange()
    {
        // Arrange
        var receivedUpdates = new List<SettingUpdate>();
        var mockStream = CreateMockStream(receivedUpdates);
        var cts = new CancellationTokenSource();

        var subscribeTask = _broadcaster.SubscribeAsync("subscriber", null, mockStream.Object, cts.Token);
        await Task.Delay(50);

        var instanceId = Guid.NewGuid();
        var setting = SdkSetting.AsText("testKey", "Test Label", "initial");

        // Act
        _broadcaster.SubscribeToSetting(instanceId, setting);
        await Task.Delay(50);

        setting.SetValue("updated");
        await Task.Delay(200);

        await cts.CancelAsync();
        await subscribeTask;

        // Assert - should receive the value change
        receivedUpdates.Should().Contain(u =>
            u.SettingKey == "testKey" &&
            u.Property == SettingProperty.Value);
    }

    [Fact]
    public async Task SubscribeToSetting_ShouldBroadcastOnLabelChange()
    {
        // Arrange
        var receivedUpdates = new List<SettingUpdate>();
        var mockStream = CreateMockStream(receivedUpdates);
        var cts = new CancellationTokenSource();

        var subscribeTask = _broadcaster.SubscribeAsync("subscriber", null, mockStream.Object, cts.Token);
        await Task.Delay(50);

        var instanceId = Guid.NewGuid();
        var setting = SdkSetting.AsText("testKey", "Initial Label", "value");

        // Act
        _broadcaster.SubscribeToSetting(instanceId, setting);
        await Task.Delay(50);

        setting.SetLabel("Updated Label");
        await Task.Delay(200);

        await cts.CancelAsync();
        await subscribeTask;

        // Assert - should receive the label change
        receivedUpdates.Should().Contain(u =>
            u.SettingKey == "testKey" &&
            u.Property == SettingProperty.Label &&
            u.StringValue == "Updated Label");
    }

    [Fact]
    public async Task SubscribeToSetting_ShouldBroadcastOnVisibilityChange()
    {
        // Arrange
        var receivedUpdates = new List<SettingUpdate>();
        var mockStream = CreateMockStream(receivedUpdates);
        var cts = new CancellationTokenSource();

        var subscribeTask = _broadcaster.SubscribeAsync("subscriber", null, mockStream.Object, cts.Token);
        await Task.Delay(50);

        var instanceId = Guid.NewGuid();
        var setting = SdkSetting.AsText("testKey", "Label", "value", isVisible: true);

        // Act
        _broadcaster.SubscribeToSetting(instanceId, setting);
        await Task.Delay(50);

        setting.SetVisibility(false);
        await Task.Delay(200);

        await cts.CancelAsync();
        await subscribeTask;

        // Assert
        receivedUpdates.Should().Contain(u =>
            u.SettingKey == "testKey" &&
            u.Property == SettingProperty.Visibility);
    }

    #endregion

    #region SubscribeToAction Tests

    [Fact]
    public async Task SubscribeToAction_ShouldBroadcastOnLabelChange()
    {
        // Arrange
        var receivedUpdates = new List<SettingUpdate>();
        var mockStream = CreateMockStream(receivedUpdates);
        var cts = new CancellationTokenSource();

        var subscribeTask = _broadcaster.SubscribeAsync("subscriber", null, mockStream.Object, cts.Token);
        await Task.Delay(50);

        var instanceId = Guid.NewGuid();
        var action = Action.Create("actionKey", "Initial Label");

        // Act
        _broadcaster.SubscribeToAction(instanceId, action);
        await Task.Delay(50);

        action.SetLabel("Updated Label");
        await Task.Delay(200);

        await cts.CancelAsync();
        await subscribeTask;

        // Assert
        receivedUpdates.Should().Contain(u =>
            u.SettingKey == "actionKey" &&
            u.Property == SettingProperty.ActionLabel);
    }

    [Fact]
    public async Task SubscribeToAction_ShouldBroadcastOnEnabledChange()
    {
        // Arrange
        var receivedUpdates = new List<SettingUpdate>();
        var mockStream = CreateMockStream(receivedUpdates);
        var cts = new CancellationTokenSource();

        var subscribeTask = _broadcaster.SubscribeAsync("subscriber", null, mockStream.Object, cts.Token);
        await Task.Delay(50);

        var instanceId = Guid.NewGuid();
        var action = Action.Create("actionKey", "Label", isEnabled: true);

        // Act
        _broadcaster.SubscribeToAction(instanceId, action);
        await Task.Delay(50);

        action.SetEnabled(false);
        await Task.Delay(200);

        await cts.CancelAsync();
        await subscribeTask;

        // Assert
        receivedUpdates.Should().Contain(u =>
            u.SettingKey == "actionKey" &&
            u.Property == SettingProperty.ActionEnabled);
    }

    #endregion

    #region UnsubscribeModuleInstance Tests

    [Fact]
    public async Task UnsubscribeModuleInstance_ShouldStopBroadcasting()
    {
        // Arrange
        var receivedUpdates = new List<SettingUpdate>();
        var mockStream = CreateMockStream(receivedUpdates);
        var cts = new CancellationTokenSource();

        var subscribeTask = _broadcaster.SubscribeAsync("subscriber", null, mockStream.Object, cts.Token);
        await Task.Delay(50);

        var instanceId = Guid.NewGuid();
        var setting = SdkSetting.AsText("key", "Label", "initial");

        _broadcaster.SubscribeToSetting(instanceId, setting);
        await Task.Delay(50);

        // Act
        _broadcaster.UnsubscribeModuleInstance(instanceId);

        setting.SetValue("after-unsubscribe");
        await Task.Delay(100);

        await cts.CancelAsync();
        await subscribeTask;

        // Assert - should not receive updates after unsubscribe
        receivedUpdates.Should().NotContain(u => u.StringValue == "after-unsubscribe");
    }

    [Fact]
    public void UnsubscribeModuleInstance_NonExistentInstance_ShouldNotThrow()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var act = () => _broadcaster.UnsubscribeModuleInstance(nonExistentId);

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_ShouldCleanupResources()
    {
        // Arrange
        var broadcaster = new SettingUpdateBroadcaster(
            _mockSessionManager.Object,
            NullLogger<SettingUpdateBroadcaster>.Instance,
            Options.Create(new Configuration())
        );

        // Act
        var act = () => broadcaster.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldBeIdempotent()
    {
        // Arrange
        var broadcaster = new SettingUpdateBroadcaster(
            _mockSessionManager.Object,
            NullLogger<SettingUpdateBroadcaster>.Instance,
            Options.Create(new Configuration())
        );

        // Act
        var act = () =>
        {
            broadcaster.Dispose();
            broadcaster.Dispose();
        };

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    private static Mock<IServerStreamWriter<SettingUpdate>> CreateMockStream(List<SettingUpdate> receivedUpdates)
    {
        var mock = new Mock<IServerStreamWriter<SettingUpdate>>();
        mock.Setup(s => s.WriteAsync(It.IsAny<SettingUpdate>(), It.IsAny<CancellationToken>()))
            .Callback<SettingUpdate, CancellationToken>((u, _) => receivedUpdates.Add(u))
            .Returns(Task.CompletedTask);
        return mock;
    }
}