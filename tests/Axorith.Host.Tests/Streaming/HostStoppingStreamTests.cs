using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Streaming;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Axorith.Host.Tests.Streaming;

public sealed class HostStoppingStreamTests
{
    [Fact]
    public async Task HostStoppingCompletesAllOpenStreamsWithoutWaitingForClientDisconnect()
    {
        using var stopping = new CancellationTokenSource();
        var lifetime = new Mock<IHostApplicationLifetime>();
        lifetime.SetupGet(value => value.ApplicationStopping).Returns(stopping.Token);
        var manager = Mock.Of<ISessionManager>();
        using var notifications = new NotificationBroadcaster(NullLogger<NotificationBroadcaster>.Instance, lifetime.Object);
        using var sessions = new SessionEventBroadcaster(manager, NullLogger<SessionEventBroadcaster>.Instance, lifetime.Object);
        using var settings = new SettingUpdateBroadcaster(manager, NullLogger<SettingUpdateBroadcaster>.Instance,
            Options.Create(new Axorith.Host.Configuration()), lifetime.Object);
        var streams = new[]
        {
            notifications.SubscribeAsync("notification", Mock.Of<IServerStreamWriter<NotificationEvent>>(), CancellationToken.None),
            sessions.SubscribeAsync("session", Mock.Of<IServerStreamWriter<SessionEvent>>(), CancellationToken.None),
            settings.SubscribeAsync("setting", null, Mock.Of<IServerStreamWriter<SettingUpdate>>(), CancellationToken.None)
        };
        Assert.All(streams, stream => Assert.False(stream.IsCompleted));
        Assert.True(notifications.HasSubscribers);

        await stopping.CancelAsync();
        await Task.WhenAll(streams).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.All(streams, stream => Assert.True(stream.IsCompletedSuccessfully));
        Assert.False(notifications.HasSubscribers);
    }
}
