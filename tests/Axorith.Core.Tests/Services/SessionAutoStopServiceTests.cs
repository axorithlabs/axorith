using Axorith.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Core.Tests.Services;

public sealed class SessionAutoStopServiceTests
{
    [Fact]
    public async Task StartTrackingAsync_WithExpiredDeadline_TracksImmediateStop()
    {
        var service = new SessionAutoStopService(null!, null!, null!,
            NullLogger<SessionAutoStopService>.Instance);

        await service.StartTrackingAsync(Guid.NewGuid(), TimeSpan.Zero, null);

        Assert.Equal(TimeSpan.Zero, service.GetTimeRemaining());
        await service.StopTrackingAsync();
    }
}
