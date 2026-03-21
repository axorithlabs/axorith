namespace Axorith.Shared.Platform.MacOS;

/// <summary>
///     Pure utility for generating LaunchAgent plist XML.
///     Extracted from MacOSAutoStartManager to allow cross-platform testability.
/// </summary>
internal static class LaunchAgentPlistGenerator
{
	private const string TrayArgument = "--tray";

	/// <summary>
	///     Generates a LaunchAgent plist XML document.
	/// </summary>
	internal static string Generate(string label, string executablePath, bool startMinimized)
	{
		var programArguments = startMinimized
			? $"<string>{EscapeXml(executablePath)}</string><string>{TrayArgument}</string>"
			: $"<string>{EscapeXml(executablePath)}</string>";

		return $"""
			<?xml version="1.0" encoding="UTF-8"?>
			<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
			<plist version="1.0">
			<dict>
			    <key>Label</key>
			    <string>{label}</string>
			    <key>ProgramArguments</key>
			    <array>
			        {programArguments}
			    </array>
			    <key>RunAtLoad</key>
			    <true/>
			    <key>KeepAlive</key>
			    <false/>
			</dict>
			</plist>
			""";
	}

	private static string EscapeXml(string value)
	{
		return value
			.Replace("&", "&amp;")
			.Replace("<", "&lt;")
			.Replace(">", "&gt;")
			.Replace("\"", "&quot;")
			.Replace("'", "&apos;");
	}
}
