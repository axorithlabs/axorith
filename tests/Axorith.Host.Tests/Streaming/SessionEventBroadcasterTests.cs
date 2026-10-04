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

    public void Dispose() => _broadcaster.Dispose();


    [Fact]
    public void Constructor_ShouldSubscribeToSessionEvents()
    {
        _capturedSessionStarted.Should().NotBeNull();
        _capturedSessionStopped.Should().NotBeNull();
    }



    [Fact]
    public async Task SubscribeAsync_WithValidSubscriberId_ShouldAddSubscriber()
    {
        var mockStream = new Mock<IServerStreamWriter<SessionEvent>>();
        var cts = new CancellationTokenSource();

        var subscribeTask = _broadcaster.SubscribeAsync("subscriber-1", mockStream.Object, cts.Token);

        // Give it time to register
        await Task.Delay(50);

        // Cancel to unsubscribe
        await cts.CancelAsync();

        var completed = await Task.WhenAny(subscribeTask, Task.Delay(TimeSpan.FromSeconds(1))) == subscribeTask;
        completed.Should().BeTrue();
    }

    [Fact]
    public async Task SubscribeAsync_WithNullSubscriberId_ShouldThrow()
    {
        var mockStream = new Mock<IServerStreamWriter<SessionEvent>>();

        var act = async () => await _broadcaster.SubscribeAsync(null!, mockStream.Object, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SubscribeAsync_WithEmptySubscriberId_ShouldThrow()
    {
        var mockStream = new Mock<IServerStreamWriter<SessionEvent>>();

        var act = async () => await _broadcaster.SubscribeAsync("", mockStream.Object, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SubscribeAsync_WithNullStream_ShouldThrow()
    {
        var act = async () => await _broadcaster.SubscribeAsync("subscriber-1", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SubscribeAsync_MultipleSubscribers_ShouldAllReceiveEvents()
    {
        var receivedEvents1 = new List<SessionEvent>();
        var receivedEvents2 = new List<SessionEvent>();

        var mockStream1 = CreateMockStream(receivedEvents1);
        var mockStream2 = CreateMockStream(receivedEvents2);

        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();

        var sub1 = _broadcaster.SubscribeAsync("sub-1", mockStream1.Object, cts1.Token);
        var sub2 = _broadcaster.SubscribeAsync("sub-2", mockStream2.Object, cts2.Token);

        await Task.Delay(50);

        var presetId = Guid.NewGuid();
        _capturedSessionStarted?.Invoke(presetId);

        await Task.Delay(100);

        // Cleanup
        await cts1.CancelAsync();
        await cts2.CancelAsync();

        await Task.WhenAll(sub1, sub2);
        receivedEvents1.Should().ContainSingle(e => e.Type == SessionEventType.SessionEventStarted);
        receivedEvents2.Should().ContainSingle(e => e.Type == SessionEventType.SessionEventStarted);
    }

    [Fact]
    public async Task SubscribeAsync_DuplicateSubscriberId_ShouldReplaceStream()
    {
        var receivedEvents1 = new List<SessionEvent>();
        var receivedEvents2 = new List<SessionEvent>();

        var mockStream1 = CreateMockStream(receivedEvents1);
        var mockStream2 = CreateMockStream(receivedEvents2);

        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();

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

        receivedEvents2.Should().NotBeEmpty();
    }



    [Fact]
    public async Task OnSessionStarted_ShouldBroadcastStartedEvent()
    {
        var receivedEvents = new List<SessionEvent>();
        var mockStream = CreateMockStream(receivedEvents);
        var cts = new CancellationTokenSource();

        var subscribeTask = _broadcaster.SubscribeAsync("subscriber", mockStream.Object, cts.Token);
        await Task.Delay(50);

        var presetId = Guid.NewGuid();

        _capturedSessionStarted?.Invoke(presetId);
        await Task.Delay(100);

        await cts.CancelAsync();
        await subscribeTask;

        receivedEvents.Should().ContainSingle();
        receivedEvents[0].Type.Should().Be(SessionEventType.SessionEventStarted);
        receivedEvents[0].PresetId.Should().Be(presetId.ToString());
    }

    [Fact]
    public async Task OnSessionStopped_ShouldBroadcastStoppedEvent()
    {
        var receivedEvents = new List<SessionEvent>();
        var mockStream = CreateMockStream(receivedEvents);
        var cts = new CancellationTokenSource();

        var subscribeTask = _broadcaster.SubscribeAsync("subscriber", mockStream.Object, cts.Token);
        await Task.Delay(50);

        var presetId = Guid.NewGuid();

        _capturedSessionStopped?.Invoke(presetId);
        await Task.Delay(100);

        await cts.CancelAsync();
        await subscribeTask;

        receivedEvents.Should().ContainSingle();
        receivedEvents[0].Type.Should().Be(SessionEventType.SessionEventStopped);
        receivedEvents[0].PresetId.Should().Be(presetId.ToString());
    }

    [Fact]
    public async Task Broadcast_WhenStreamFails_ShouldRemoveSubscriber()
    {
        var mockStream = new Mock<IServerStreamWriter<SessionEvent>>();
        mockStream.Setup(s => s.WriteAsync(It.IsAny<SessionEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Stream closed"));

        var cts = new CancellationTokenSource();
        var subscribeTask = _broadcaster.SubscribeAsync("failing-subscriber", mockStream.Object, cts.Token);
        await Task.Delay(50);

        _capturedSessionStarted?.Invoke(Guid.NewGuid());
        await Task.Delay(100);

        await cts.CancelAsync();

        var completed = await Task.WhenAny(subscribeTask, Task.Delay(TimeSpan.FromSeconds(1))) == subscribeTask;
        completed.Should().BeTrue();
    }



    [Fact]
    public void Dispose_ShouldUnsubscribeFromEvents()
    {
        var mockSessionManager = new Mock<ISessionManager>();
        var broadcaster = new SessionEventBroadcaster(
            mockSessionManager.Object,
            NullLogger<SessionEventBroadcaster>.Instance
        );

        broadcaster.Dispose();

        mockSessionManager.VerifyRemove(m => m.SessionStarted -= It.IsAny<Action<Guid>>(), Times.Once);
        mockSessionManager.VerifyRemove(m => m.SessionStopped -= It.IsAny<Action<Guid>>(), Times.Once);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldBeIdempotent()
    {
        var broadcaster = new SessionEventBroadcaster(
            _mockSessionManager.Object,
            NullLogger<SessionEventBroadcaster>.Instance
        );

        var act = () =>
        {
            broadcaster.Dispose();
            broadcaster.Dispose();
        };

        act.Should().NotThrow();
    }


    private static Mock<IServerStreamWriter<SessionEvent>> CreateMockStream(List<SessionEvent> receivedEvents)
    {
        var mock = new Mock<IServerStreamWriter<SessionEvent>>();
        mock.Setup(s => s.WriteAsync(It.IsAny<SessionEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SessionEvent, CancellationToken>((e, _) => receivedEvents.Add(e))
            .Returns(Task.CompletedTask);
        return mock;
    }
}
