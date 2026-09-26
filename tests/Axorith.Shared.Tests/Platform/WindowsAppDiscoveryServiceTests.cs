using System.Runtime.Versioning;
using Axorith.Shared.Platform.Windows;
using Axorith.Shared.Platform;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Shared.Tests.Platform;

public class WindowsAppDiscoveryServiceTests
{
    [Fact]
    [SupportedOSPlatform("windows")]
    public void InstalledChromeAppearsInLauncherChoices()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var chrome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Google", "Chrome", "Application", "chrome.exe");
        if (!File.Exists(chrome))
            return;

        var service = new WindowsAppDiscoveryService(NullLogger<WindowsAppDiscoveryService>.Instance);
        var choices = ApplicationSelector.GetInstalledChoices(service, app => app.ExecutablePath,
            ApplicationSelector.IsSupportedLauncherApp);
        choices.Should().Contain(choice => choice.Key.Equals(chrome, StringComparison.OrdinalIgnoreCase));
    }

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
