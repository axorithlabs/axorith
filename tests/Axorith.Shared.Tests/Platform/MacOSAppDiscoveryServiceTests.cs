using System.Runtime.Versioning;
using Axorith.Shared.Platform;
using Axorith.Shared.Platform.MacOS;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Shared.Tests.Platform;

/// <summary>
///     Tests for MacOSAppDiscoveryService.
///     Tests that exercise macOS-specific APIs are guarded with OperatingSystem.IsMacOS().
///     Platform-agnostic logic (PList parsing) is tested via helper methods.
/// </summary>
public class MacOsAppDiscoveryServiceTests
{
	[Fact]
	public void FindKnownApp_ShouldReturnNull_WhenAppNotFound()
	{
		if (!OperatingSystem.IsMacOS())
		{
			return;
		}

		var service = CreateService();
		var result = service.FindKnownApp("completely_fake_nonexistent_app_xyz");

		result.Should().BeNull();
	}

	[Fact]
	public void FindAppsByPublisher_ShouldReturnEmpty_WhenNoMatch()
	{
		if (!OperatingSystem.IsMacOS())
		{
			return;
		}

		var service = CreateService();
		var results = service.FindAppsByPublisher("NonExistentPublisher12345");

		results.Should().BeEmpty();
	}

	[Fact]
	public void GetInstalledApplicationsIndex_ShouldReturnCachedResult_WithinTTL()
	{
		if (!OperatingSystem.IsMacOS())
		{
			return;
		}

		var service = CreateService();

		// First call builds the index
		var first = service.GetInstalledApplicationsIndex();

		// Second call should return cached result
		var second = service.GetInstalledApplicationsIndex();

		first.Should().NotBeNull();
		second.Should().NotBeNull();
		first.Count.Should().Be(second.Count,
			"cached results should return the same count");
	}

	[Fact]
	public void FindKnownApp_ShouldBeCaseInsensitive()
	{
		if (!OperatingSystem.IsMacOS())
		{
			return;
		}

		var service = CreateService();

		var upperResult = service.FindKnownApp("SAFARI");
		var lowerResult = service.FindKnownApp("safari");

		// Both should return the same result (or both null)
		if (upperResult != null)
		{
			upperResult.Should().Be(lowerResult,
				"case-insensitive search should return the same path");
		}
	}

	[Fact]
	public void FindAppsByPublisher_ShouldMatchApple()
	{
		if (!OperatingSystem.IsMacOS())
		{
			return;
		}

		var service = CreateService();
		var results = service.FindAppsByPublisher("apple");

		// On macOS, Apple apps should be discoverable
		results.Should().NotBeEmpty("Apple publishes bundled apps on macOS");
	}

	[Fact]
	public void GetInstalledApplicationsIndex_ShouldFindSystemApps()
	{
		if (!OperatingSystem.IsMacOS())
		{
			return;
		}

		var service = CreateService();
		var index = service.GetInstalledApplicationsIndex();

		index.Should().NotBeEmpty("macOS has system applications in /Applications");
		index.Should().Contain(a => a.Name.Contains("Safari", StringComparison.OrdinalIgnoreCase) ||
		                          a.Name.Contains("Finder", StringComparison.OrdinalIgnoreCase),
			"system apps like Safari or Finder should be discoverable");
	}

	[Fact]
	public void GetInstalledApplicationsIndex_ShouldReturnSortedByName()
	{
		if (!OperatingSystem.IsMacOS())
		{
			return;
		}

		var service = CreateService();
		var index = service.GetInstalledApplicationsIndex();

		if (index.Count > 1)
		{
			var sorted = index.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
			index.Should().Equal(sorted, "results should be sorted by name");
		}
	}

	[Fact]
	public void GetInstalledApplicationsIndex_AppInfoRecord_HasCorrectProperties()
	{
		// Test that AppInfo record is properly structured (platform-agnostic)
		var appInfo = new AppInfo("Test App", "/usr/bin/test", "/test/icon.png");

		appInfo.Name.Should().Be("Test App");
		appInfo.ExecutablePath.Should().Be("/usr/bin/test");
		appInfo.IconPath.Should().Be("/test/icon.png");
	}

	[Fact]
	public void AppInfo_Equality_WorksCorrectly()
	{
		var app1 = new AppInfo("Safari", "/Applications/Safari.app/Contents/MacOS/Safari", "");
		var app2 = new AppInfo("Safari", "/Applications/Safari.app/Contents/MacOS/Safari", "");

		app1.Should().Be(app2, "records with same values should be equal");
	}

	[SupportedOSPlatform("macos")]
	private static MacOsAppDiscoveryService CreateService()
	{
		return new MacOsAppDiscoveryService(
			NullLogger<MacOsAppDiscoveryService>.Instance);
	}
}
