using Axorith.Shared.Utils;

namespace Axorith.Shared.Tests.Utils;

public sealed class DeviceIdProviderTests
{
    [Fact]
    public async Task GetDeviceId_ReturnsTheSameWindowsIdentityForConcurrentCallers()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            Assert.Throws<PlatformNotSupportedException>(DeviceIdProvider.GetDeviceId);
            return;
        }

        var ids = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(DeviceIdProvider.GetDeviceId)));

        Assert.All(ids, id => Assert.Equal(ids[0], id));
        Assert.Matches("^[0-9a-f]+$", ids[0]);
        Assert.NotEqual(Environment.MachineName, ids[0]);
    }
}
