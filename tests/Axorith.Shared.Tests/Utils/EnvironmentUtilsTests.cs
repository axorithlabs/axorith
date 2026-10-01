using System.Runtime.InteropServices;
using Axorith.Shared.Utils;

namespace Axorith.Shared.Tests.Utils;

public sealed class EnvironmentUtilsTests
{
    [Fact]
    public void GetCurrentPlatformMatchesTheOperatingSystemRunningTheTest()
    {
        var expected = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Sdk.Platform.Windows
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? Sdk.Platform.Linux
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? Sdk.Platform.MacOs
                    : throw new PlatformNotSupportedException("Axorith has no platform mapping for this test host.");

        Assert.Equal(expected, EnvironmentUtils.GetCurrentPlatform());
    }
}
