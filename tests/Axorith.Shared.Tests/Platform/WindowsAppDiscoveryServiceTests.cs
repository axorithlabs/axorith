using System.Runtime.Versioning;
using Axorith.Shared.Platform.Windows;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Shared.Tests.Platform;

public class WindowsAppDiscoveryServiceTests
{
    [Fact]
    [SupportedOSPlatform("windows")]
    public void FindKnownApp_ShouldFallbackToDriveScan()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            var service = new WindowsAppDiscoveryService(
                NullLogger<WindowsAppDiscoveryService>.Instance,
                [tempRoot]);

            var result = service.FindKnownApp("cs2");

            result.Should().NotBeEmpty();
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, true);
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }
}
