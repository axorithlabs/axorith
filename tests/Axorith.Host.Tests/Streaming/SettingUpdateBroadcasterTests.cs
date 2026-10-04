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
    private readonly Mock<ISessionManager> _mockSessionManager = new();
    private readonly SettingUpdateBroadcaster _broadcaster;

    public SettingUpdateBroadcasterTests()
    {
        _broadcaster = CreateBroadcaster();
    }

    public void Dispose() => _broadcaster.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubscribeAsync_WithValidParameters_ShouldComplete(bool filtered)
    {
        var stream = new Mock<IServerStreamWriter<SettingUpdate>>();
        using var cts = new CancellationTokenSource();
        var moduleId = filtered ? Guid.NewGuid().ToString() : null;

        var task = _broadcaster.SubscribeAsync("subscriber", moduleId, stream.Object, cts.Token);
        await Task.Delay(50);
        await cts.CancelAsync();

        (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2)))).Should().Be(task);
    }

    [Fact]
    public async Task SubscribeAsync_WithNullSubscriberId_ShouldThrow()
    {
        var stream = new Mock<IServerStreamWriter<SettingUpdate>>();
        var act = () => _broadcaster.SubscribeAsync(null!, null, stream.Object, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SubscribeAsync_WithNullStream_ShouldThrow()
    {
        var act = () => _broadcaster.SubscribeAsync("subscriber", null, null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SubscribeAsync_DuplicateSubscriber_ShouldReplace()
    {
        var stream1 = new Mock<IServerStreamWriter<SettingUpdate>>();
        var stream2 = new Mock<IServerStreamWriter<SettingUpdate>>();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();

        var first = _broadcaster.SubscribeAsync("same-id", null, stream1.Object, cts1.Token);
        await Task.Delay(50);
        var second = _broadcaster.SubscribeAsync("same-id", null, stream2.Object, cts2.Token);
        await Task.Delay(50);

        await cts1.CancelAsync();
        await cts2.CancelAsync();
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task BroadcastUpdateAsync_WithNoSubscribers_ShouldNotThrow()
    {
        var act = () => _broadcaster.BroadcastUpdateAsync(
            Guid.NewGuid(), "settingKey", SettingProperty.Value, "newValue");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task BroadcastUpdateAsync_ToSubscriber_ShouldSendUpdate()
    {
        var updates = await CaptureAsync(instanceId =>
            _broadcaster.BroadcastUpdateAsync(instanceId, "settingKey", SettingProperty.Value, "newValue"));

        updates.Should().ContainSingle();
        updates[0].SettingKey.Should().Be("settingKey");
    }

    [Fact]
    public async Task BroadcastUpdateAsync_WithLabelProperty_ShouldIncludeLabel()
    {
        var updates = await CaptureAsync(instanceId =>
            _broadcaster.BroadcastUpdateAsync(instanceId, "key", SettingProperty.Label, "New Label"));

        updates.Should().ContainSingle();
        updates[0].Property.Should().Be(SettingProperty.Label);
        updates[0].StringValue.Should().Be("New Label");
    }

    [Fact]
    public async Task BroadcastUpdateAsync_ToFilteredSubscriber_ShouldOnlyReceiveMatchingUpdates()
    {
        var updates = await CaptureAsync(async instanceId =>
        {
            await _broadcaster.BroadcastUpdateAsync(instanceId, "key", SettingProperty.Value, "target");
            await _broadcaster.BroadcastUpdateAsync(Guid.NewGuid(), "key", SettingProperty.Value, "other");
        }, filterToInstance: true);

        updates.Should().ContainSingle(update => update.StringValue == "target");
    }

    [Fact]
    public async Task SubscribeToSetting_ShouldBroadcastOnValueChange()
    {
        var updates = await CaptureAsync(async instanceId =>
        {
            var setting = SdkSetting.AsText("testKey", "Test Label", "initial");
            _broadcaster.SubscribeToSetting(instanceId, setting);
            await Task.Delay(50);
            setting.SetValue("updated");
        }, settleMs: 200);

        updates.Should().Contain(update => update.SettingKey == "testKey" && update.Property == SettingProperty.Value);
    }

    [Fact]
    public async Task SubscribeToSetting_ShouldBroadcastOnLabelChange()
    {
        var updates = await CaptureAsync(async instanceId =>
        {
            var setting = SdkSetting.AsText("testKey", "Initial Label", "value");
            _broadcaster.SubscribeToSetting(instanceId, setting);
            await Task.Delay(50);
            setting.SetLabel("Updated Label");
        }, settleMs: 200);

        updates.Should().Contain(update => update.SettingKey == "testKey" &&
                                           update.Property == SettingProperty.Label &&
                                           update.StringValue == "Updated Label");
    }

    [Fact]
    public async Task SubscribeToSetting_ShouldBroadcastOnVisibilityChange()
    {
        var updates = await CaptureAsync(async instanceId =>
        {
            var setting = SdkSetting.AsText("testKey", "Label", "value", isVisible: true);
            _broadcaster.SubscribeToSetting(instanceId, setting);
            await Task.Delay(50);
            setting.SetVisibility(false);
        }, settleMs: 200);

        updates.Should().Contain(update => update.SettingKey == "testKey" &&
                                           update.Property == SettingProperty.Visibility);
    }

    [Fact]
    public async Task SubscribeToAction_ShouldBroadcastOnLabelChange()
    {
        var updates = await CaptureAsync(async instanceId =>
        {
            var action = new Action("actionKey", "Initial Label");
            _broadcaster.SubscribeToAction(instanceId, action);
            await Task.Delay(50);
            action.SetLabel("Updated Label");
        }, settleMs: 200);

        updates.Should().Contain(update => update.SettingKey == "actionKey" &&
                                           update.Property == SettingProperty.ActionLabel);
    }

    [Fact]
    public async Task SubscribeToAction_ShouldBroadcastOnEnabledChange()
    {
        var updates = await CaptureAsync(async instanceId =>
        {
            var action = new Action("actionKey", "Label", isEnabled: true);
            _broadcaster.SubscribeToAction(instanceId, action);
            await Task.Delay(50);
            action.SetEnabled(false);
        }, settleMs: 200);

        updates.Should().Contain(update => update.SettingKey == "actionKey" &&
                                           update.Property == SettingProperty.ActionEnabled);
    }

    [Fact]
    public async Task UnsubscribeModuleInstance_ShouldStopBroadcasting()
    {
        var updates = await CaptureAsync(async instanceId =>
        {
            var setting = SdkSetting.AsText("key", "Label", "initial");
            _broadcaster.SubscribeToSetting(instanceId, setting);
            await Task.Delay(50);
            _broadcaster.UnsubscribeModuleInstance(instanceId);
            setting.SetValue("after-unsubscribe");
        });

        updates.Should().NotContain(update => update.StringValue == "after-unsubscribe");
    }

    [Fact]
    public void UnsubscribeModuleInstance_NonExistentInstance_ShouldNotThrow()
    {
        var act = () => _broadcaster.UnsubscribeModuleInstance(Guid.NewGuid());
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Dispose_ShouldBeSafeAndIdempotent(int calls)
    {
        var broadcaster = CreateBroadcaster();
        var act = () =>
        {
            for (var i = 0; i < calls; i++)
                broadcaster.Dispose();
        };
        act.Should().NotThrow();
    }

    private SettingUpdateBroadcaster CreateBroadcaster() => new(
        _mockSessionManager.Object,
        NullLogger<SettingUpdateBroadcaster>.Instance,
        Options.Create(new Configuration()));

    private async Task<List<SettingUpdate>> CaptureAsync(
        Func<Guid, Task> exercise,
        bool filterToInstance = false,
        int settleMs = 100)
    {
        var updates = new List<SettingUpdate>();
        var stream = CreateMockStream(updates);
        using var cts = new CancellationTokenSource();
        var instanceId = Guid.NewGuid();
        var subscription = _broadcaster.SubscribeAsync(
            "subscriber",
            filterToInstance ? instanceId.ToString() : null,
            stream.Object,
            cts.Token);

        await Task.Delay(50);
        await exercise(instanceId);
        await Task.Delay(settleMs);
        await cts.CancelAsync();
        await subscription;
        return updates;
    }

    private static Mock<IServerStreamWriter<SettingUpdate>> CreateMockStream(List<SettingUpdate> updates)
    {
        var mock = new Mock<IServerStreamWriter<SettingUpdate>>();
        mock.Setup(stream => stream.WriteAsync(It.IsAny<SettingUpdate>(), It.IsAny<CancellationToken>()))
            .Callback<SettingUpdate, CancellationToken>((update, _) => updates.Add(update))
            .Returns(Task.CompletedTask);
        return mock;
    }
}
