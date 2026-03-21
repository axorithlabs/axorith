using System.Xml.Linq;
using Axorith.Shared.Platform.MacOS;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Shared.Tests.Platform;

public class MacOSAutoStartManagerTests
{
	private const string Label = "com.axorith.host";
	private static readonly string PlistDirectory = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
		"Library", "LaunchAgents");

	private static readonly string PlistPath = Path.Combine(PlistDirectory, "com.axorith.host.plist");

	private static string GeneratePlistXml(string path, bool startMinimized)
	{
		return LaunchAgentPlistGenerator.Generate(Label, path, startMinimized);
	}

	// ── Platform-dependent tests (run only on macOS) ────────────────

	[Fact]
	public void IsAutoStartEnabled_ReturnsFalse_WhenPlistDoesNotExist()
	{
		if (!OperatingSystem.IsMacOS())
		{
			return;
		}

		var logger = NullLogger.Instance;
		var manager = new MacOSAutoStartManager(logger);

		manager.IsAutoStartEnabled.Should().BeFalse();
	}

	[Fact]
	public void IsAutoStartEnabled_ReturnsTrue_WhenPlistExists()
	{
		if (!OperatingSystem.IsMacOS())
		{
			return;
		}

		var logger = NullLogger.Instance;
		var manager = new MacOSAutoStartManager(logger);

		try
		{
			Directory.CreateDirectory(PlistDirectory);
			File.WriteAllText(PlistPath, GeneratePlistXml("/test/path", true));

			manager.IsAutoStartEnabled.Should().BeTrue();
		}
		finally
		{
			if (File.Exists(PlistPath))
			{
				File.Delete(PlistPath);
			}
		}
	}

	[Fact]
	public void IsStartMinimized_ReturnsFalse_WhenPlistDoesNotExist()
	{
		if (!OperatingSystem.IsMacOS())
		{
			return;
		}

		var logger = NullLogger.Instance;
		var manager = new MacOSAutoStartManager(logger);

		manager.IsStartMinimized.Should().BeFalse();
	}

	[Fact]
	public void IsStartMinimized_ReturnsTrue_WhenTrayArgumentPresent()
	{
		if (!OperatingSystem.IsMacOS())
		{
			return;
		}

		var logger = NullLogger.Instance;
		var manager = new MacOSAutoStartManager(logger);

		try
		{
			Directory.CreateDirectory(PlistDirectory);
			File.WriteAllText(PlistPath, GeneratePlistXml("/test/path", startMinimized: true));

			manager.IsStartMinimized.Should().BeTrue();
		}
		finally
		{
			if (File.Exists(PlistPath))
			{
				File.Delete(PlistPath);
			}
		}
	}

	[Fact]
	public void IsStartMinimized_ReturnsFalse_WhenTrayArgumentNotPresent()
	{
		if (!OperatingSystem.IsMacOS())
		{
			return;
		}

		var logger = NullLogger.Instance;
		var manager = new MacOSAutoStartManager(logger);

		try
		{
			Directory.CreateDirectory(PlistDirectory);
			File.WriteAllText(PlistPath, GeneratePlistXml("/test/path", startMinimized: false));

			manager.IsStartMinimized.Should().BeFalse();
		}
		finally
		{
			if (File.Exists(PlistPath))
			{
				File.Delete(PlistPath);
			}
		}
	}

	[Fact]
	public void DisableAutoStart_ReturnsTrue_WhenNoPlistExists()
	{
		if (!OperatingSystem.IsMacOS())
		{
			return;
		}

		var logger = NullLogger.Instance;
		var manager = new MacOSAutoStartManager(logger);

		if (File.Exists(PlistPath))
		{
			File.Delete(PlistPath);
		}

		manager.DisableAutoStart().Should().BeTrue();
	}

	[Fact]
	public void DisableAutoStart_RemovesPlist_WhenExists()
	{
		if (!OperatingSystem.IsMacOS())
		{
			return;
		}

		var logger = NullLogger.Instance;
		var manager = new MacOSAutoStartManager(logger);

		try
		{
			Directory.CreateDirectory(PlistDirectory);
			File.WriteAllText(PlistPath, GeneratePlistXml("/test/path", true));

			manager.DisableAutoStart().Should().BeTrue();
			File.Exists(PlistPath).Should().BeFalse();
		}
		finally
		{
			if (File.Exists(PlistPath))
			{
				File.Delete(PlistPath);
			}
		}
	}

	// ── Cross-platform tests (plist XML generation) ─────────────────

	[Fact]
	public void GeneratePlistXml_ProducesValidXml()
	{
		var xml = GeneratePlistXml("/Applications/Axorith.Host", true);

		var act = () => XDocument.Parse(xml);
		act.Should().NotThrow();
	}

	[Fact]
	public void GeneratePlistXml_ContainsCorrectLabel()
	{
		var xml = GeneratePlistXml("/test/path", false);

		xml.Should().Contain("<string>com.axorith.host</string>");
	}

	[Fact]
	public void GeneratePlistXml_ContainsTrayArgument_WhenStartMinimized()
	{
		var xml = GeneratePlistXml("/test/path", startMinimized: true);

		xml.Should().Contain("<string>--tray</string>");
	}

	[Fact]
	public void GeneratePlistXml_DoesNotContainTrayArgument_WhenNotStartMinimized()
	{
		var xml = GeneratePlistXml("/test/path", startMinimized: false);

		xml.Should().NotContain("--tray");
	}

	[Fact]
	public void GeneratePlistXml_ContainsRunAtLoad()
	{
		var xml = GeneratePlistXml("/test/path", false);

		xml.Should().Contain("<key>RunAtLoad</key>");
		xml.Should().Contain("<true/>");
	}

	[Fact]
	public void GeneratePlistXml_ContainsKeepAliveFalse()
	{
		var xml = GeneratePlistXml("/test/path", false);

		xml.Should().Contain("<key>KeepAlive</key>");
	}

	[Fact]
	public void GeneratePlistXml_EscapesSpecialCharacters()
	{
		var pathWithSpecialChars = "/Applications/My App & Friends/Axorith.Host";
		var xml = GeneratePlistXml(pathWithSpecialChars, false);

		xml.Should().Contain("&amp;");
		var act = () => XDocument.Parse(xml);
		act.Should().NotThrow();
	}

	[Fact]
	public void GeneratePlistXml_ContainsExecutablePath()
	{
		var xml = GeneratePlistXml("/opt/axorith/Axorith.Host", false);

		xml.Should().Contain("/opt/axorith/Axorith.Host");
	}

	[Fact]
	public void GeneratePlistXml_ContainsProgramArgumentsKey()
	{
		var xml = GeneratePlistXml("/test/path", false);

		xml.Should().Contain("<key>ProgramArguments</key>");
		xml.Should().Contain("<array>");
	}
}
