using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Streaming;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Axorith.Host.Tests.Streaming;

public class SessionEventBroadcasterTests : IDisposable
{
    private readonly Mock<ISessionManager> _mockSessionManager;
    private readonly SessionEventBroadcaster _broadcaster;
    private Action<Guid>? _capturedSessionStarted;
    private Action<Guid>? _capturedSessionStopped;

    public SessionEventBroadcasterTests()
    {
        _mockSessionManager = new Mock<ISessionManager>();

        // Capture event subscriptions
        _mockSessionManager.SetupAdd(m => m.SessionStarted += It.IsAny<Action<Guid>>())
            .Callback<Action<Guid>>(handler => _capturedSessionStarted = handler);
        _mockSessionManager.SetupAdd(m => m.SessionStopped += It.IsAny<Action<Guid>>())
            .Callback<Action<Guid>>(handler => _capturedSessionStopped = handler);

        _broadcaster = new SessionEventBroadcaster(
            _mockSessionManager.Object,
            NullLogger<SessionEventBroadcaster>.Instance
        );
    }

    public void Dispose()
    {
        _broadcaster.Dispose();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_ShouldSubscribeToSessionEvents()
    {
        // Assert
        _capturedSessionStarted.Should().NotBeNull();
        _capturedSessionStopped.Should().NotBeNull();
    }

    #endregion

    #region SubscribeAsync Tests

    [Fact]
    public async Task SubscribeAsync_WithValidSubscriberId_ShouldAddSubscriber()
    {
        // Arrange
        var mockStream = new Mock<IServerStreamWriter<SessionEvent>>();
        var cts = new CancellationTokenSource();

        // Act
        var subscribeTask = _broadcaster.SubscribeAsync("subscriber-1", mockStream.Object, cts.Token);

        // Give it time to register
        await Task.Delay(50);

        // Cancel to unsubscribe
        await cts.CancelAsync();

        // Assert - should complete without error
        var completed = await Task.WhenAny(subscribeTask, Task.Delay(TimeSpan.FromSeconds(1))) == subscribeTask;
        completed.Should().BeTrue();
    }

    [Fact]
    public async Task SubscribeAsync_WithNullSubscriberId_ShouldThrow()
    {
        // Arrange
        var mockStream = new Mock<IServerStreamWriter<SessionEvent>>();

        // Act
        var act = async () => await _broadcaster.SubscribeAsync(null!, mockStream.Object, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SubscribeAsync_WithEmptySubscriberId_ShouldThrow()
    {
        // Arrange
        var mockStream = new Mock<IServerStreamWriter<SessionEvent>>();

        // Act
        var act = async () => await _broadcaster.SubscribeAsync("", mockStream.Object, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SubscribeAsync_WithNullStream_ShouldThrow()
    {
        // Act
        var act = async () => await _broadcaster.SubscribeAsync("subscriber-1", null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SubscribeAsync_MultipleSubscribers_ShouldAllReceiveEvents()
    {
        // Arrange
        var receivedEvents1 = new List<SessionEvent>();
        var receivedEvents2 = new List<SessionEvent>();

        var mockStream1 = CreateMockStream(receivedEvents1);
        var mockStream2 = CreateMockStream(receivedEvents2);

        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();

        var sub1 = _broadcaster.SubscribeAsync("sub-1", mockStream1.Object, cts1.Token);
        var sub2 = _broadcaster.SubscribeAsync("sub-2", mockStream2.Object, cts2.Token);

        await Task.Delay(50);

        // Act - trigger event
        var presetId = Guid.NewGuid();
        _capturedSessionStarted?.Invoke(presetId);

        await Task.Delay(100);

        // Cleanup
        await cts1.CancelAsync();
        await cts2.CancelAsync();

        // Assert
        await Task.WhenAll(sub1, sub2);
        receivedEvents1.Should().ContainSingle(e => e.Type == SessionEventType.SessionEventStarted);
        receivedEvents2.Should().ContainSingle(e => e.Type == SessionEventType.SessionEventStarted);
    }

    [Fact]
    public async Task SubscribeAsync_DuplicateSubscriberId_ShouldReplaceStream()
    {
        // Arrange
        var receivedEvents1 = new List<SessionEvent>();
        var receivedEvents2 = new List<SessionEvent>();

        var mockStream1 = CreateMockStream(receivedEvents1);
        var mockStream2 = CreateMockStream(receivedEvents2);

        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();

        // Act - subscribe twice with same ID
        var sub1 = _broadcaster.SubscribeAsync("same-id", mockStream1.Object, cts1.Token);
        await Task.Delay(50);
        var sub2 = _broadcaster.SubscribeAsync("same-id", mockStream2.Object, cts2.Token);
        await Task.Delay(50);

        // Trigger event
        _capturedSessionStarted?.Invoke(Guid.NewGuid());
        await Task.Delay(100);

        // Cleanup
        await cts1.CancelAsync();
        await cts2.CancelAsync();

        await Task.WhenAll(sub1, sub2);

        // Assert - second stream should receive event
        receivedEvents2.Should().NotBeEmpty();
    }

    #endregion

    #region Event Broadcast Tests

    [Fact]
    public async Task OnSessionStarted_ShouldBroadcastStartedEvent()
    {
        // Arrange
        var receivedEvents = new List<SessionEvent>();
        var mockStream = CreateMockStream(receivedEvents);
        var cts = new CancellationTokenSource();

        var subscribeTask = _broadcaster.SubscribeAsync("subscriber", mockStream.Object, cts.Token);
        await Task.Delay(50);

        var presetId = Guid.NewGuid();

        // Act
        _capturedSessionStarted?.Invoke(presetId);
        await Task.Delay(100);

        await cts.CancelAsync();
        await subscribeTask;

        // Assert
        receivedEvents.Should().ContainSingle();
        receivedEvents[0].Type.Should().Be(SessionEventType.SessionEventStarted);
        receivedEvents[0].PresetId.Should().Be(presetId.ToString());
    }

    [Fact]
    public async Task OnSessionStopped_ShouldBroadcastStoppedEvent()
    {
        // Arrange
        var receivedEvents = new List<SessionEvent>();
        var mockStream = CreateMockStream(receivedEvents);
        var cts = new CancellationTokenSource();

        var subscribeTask = _broadcaster.SubscribeAsync("subscriber", mockStream.Object, cts.Token);
        await Task.Delay(50);

        var presetId = Guid.NewGuid();

        // Act
        _capturedSessionStopped?.Invoke(presetId);
        await Task.Delay(100);

        await cts.CancelAsync();
        await subscribeTask;

        // Assert
        receivedEvents.Should().ContainSingle();
        receivedEvents[0].Type.Should().Be(SessionEventType.SessionEventStopped);
        receivedEvents[0].PresetId.Should().Be(presetId.ToString());
    }

    [Fact]
    public async Task Broadcast_WhenStreamFails_ShouldRemoveSubscriber()
    {
        // Arrange
        var mockStream = new Mock<IServerStreamWriter<SessionEvent>>();
        mockStream.Setup(s => s.WriteAsync(It.IsAny<SessionEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Stream closed"));

        var cts = new CancellationTokenSource();
        var subscribeTask = _broadcaster.SubscribeAsync("failing-subscriber", mockStream.Object, cts.Token);
        await Task.Delay(50);

        // Act - trigger event that will fail
        _capturedSessionStarted?.Invoke(Guid.NewGuid());
        await Task.Delay(100);

        await cts.CancelAsync();

        // Assert - task should complete (subscriber removed)
        var completed = await Task.WhenAny(subscribeTask, Task.Delay(TimeSpan.FromSeconds(1))) == subscribeTask;
        completed.Should().BeTrue();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_ShouldUnsubscribeFromEvents()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var broadcaster = new SessionEventBroadcaster(
            mockSessionManager.Object,
            NullLogger<SessionEventBroadcaster>.Instance
        );

        // Act
        broadcaster.Dispose();

        // Assert
        mockSessionManager.VerifyRemove(m => m.SessionStarted -= It.IsAny<Action<Guid>>(), Times.Once);
        mockSessionManager.VerifyRemove(m => m.SessionStopped -= It.IsAny<Action<Guid>>(), Times.Once);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldBeIdempotent()
    {
        // Arrange
        var broadcaster = new SessionEventBroadcaster(
            _mockSessionManager.Object,
            NullLogger<SessionEventBroadcaster>.Instance
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

    private static Mock<IServerStreamWriter<SessionEvent>> CreateMockStream(List<SessionEvent> receivedEvents)
    {
        var mock = new Mock<IServerStreamWriter<SessionEvent>>();
        mock.Setup(s => s.WriteAsync(It.IsAny<SessionEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SessionEvent, CancellationToken>((e, _) => receivedEvents.Add(e))
            .Returns(Task.CompletedTask);
        return mock;
    }
}