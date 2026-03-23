using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.MacOS;

/// <summary>
///     macOS implementation of auto-start management using LaunchAgent plist and launchctl.
///     - Creates plist in ~/Library/LaunchAgents/com.axorith.host.plist
///     - Uses launchctl load/unload via P/Invoke to libc.system()
///     - Auto-start is opt-in (disabled by default)
///     - Supports --tray flag for start minimized behavior
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacOsAutoStartManager(ILogger logger) : IAutoStartManager
{
	private const string Label = "com.axorith.host";
	private const string PlistFilename = "com.axorith.host.plist";
	private const string TrayArgument = "--tray";

	private static readonly string PlistDirectory = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
		"Library", "LaunchAgents");

	private static readonly string PlistPath = Path.Combine(PlistDirectory, PlistFilename);

	// ── libc P/Invoke ────────────────────────────────────────────────

	[DllImport("libc")]
	private static extern int system(string command);

	// ── IAutoStartManager ────────────────────────────────────────────

	public bool IsAutoStartEnabled
	{
		get
		{
			try
			{
				var exists = File.Exists(PlistPath);
				if (exists)
				{
					logger.LogDebug("LaunchAgent plist found at: {Path}", PlistPath);
				}

				return exists;
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Failed to check auto-start status");
				return false;
			}
		}
	}

	public bool IsStartMinimized
	{
		get
		{
			try
			{
				if (!File.Exists(PlistPath))
				{
					return false;
				}

				var content = File.ReadAllText(PlistPath);
				return content.Contains(TrayArgument, StringComparison.OrdinalIgnoreCase);
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Failed to check start minimized status");
				return false;
			}
		}
	}

	public bool EnableAutoStart(bool startMinimized = true)
	{
		try
		{
			Directory.CreateDirectory(PlistDirectory);

			var executablePath = GetExecutablePath();
			var plistXml = GeneratePlistXml(executablePath, startMinimized);

			// Validate XML before writing
			XDocument.Parse(plistXml);

			File.WriteAllText(PlistPath, plistXml);
			logger.LogInformation("LaunchAgent plist written to: {Path}", PlistPath);

			// Register with launchctl
			var result = system($"launchctl load \"{PlistPath}\"");
			if (result == 0)
			{
				logger.LogInformation("Auto-start enabled via launchctl (minimized: {Minimized})", startMinimized);
				return true;
			}

			logger.LogWarning("launchctl load returned {Result}, but plist is in place", result);
			return true; // plist exists, will be loaded on next login
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to enable auto-start");
			return false;
		}
	}

	public bool DisableAutoStart()
	{
		try
		{
			if (File.Exists(PlistPath))
			{
				// Unregister from launchctl before deleting
				var result = system($"launchctl unload \"{PlistPath}\"");
				if (result != 0)
				{
					logger.LogWarning("launchctl unload returned {Result}", result);
				}

				File.Delete(PlistPath);
				logger.LogInformation("Auto-start disabled — LaunchAgent plist removed");
			}

			return true;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to disable auto-start");
			return false;
		}
	}

	// ── Helpers ──────────────────────────────────────────────────────

	/// <summary>
	///     Gets the current executable path at runtime.
	/// </summary>
	private static string GetExecutablePath()
	{
		var exePath = Environment.ProcessPath;
		return !string.IsNullOrEmpty(exePath) ? exePath : "Axorith.Host";
	}

	private static string GeneratePlistXml(string executablePath, bool startMinimized)
	{
		return LaunchAgentPlistGenerator.Generate(Label, executablePath, startMinimized);
	}
}
