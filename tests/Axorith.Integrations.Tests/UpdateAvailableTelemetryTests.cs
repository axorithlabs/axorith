using Axorith.Client.ViewModels;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class UpdateAvailableTelemetryTests
{
    [Fact]
    public void TryTrackUpdateAvailable_TracksEachReleaseVersionOnlyOncePerProcess()
    {
        var version = $"1.2.3+{Guid.NewGuid():N}";

        Assert.True(MainViewModel.TryTrackUpdateAvailable(version));
        Assert.False(MainViewModel.TryTrackUpdateAvailable(version));
        Assert.False(MainViewModel.TryTrackUpdateAvailable(version.ToUpperInvariant()));
        Assert.True(MainViewModel.TryTrackUpdateAvailable($"1.2.4+{Guid.NewGuid():N}"));
    }
}
