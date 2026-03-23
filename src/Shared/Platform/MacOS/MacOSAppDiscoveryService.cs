using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.MacOS;

/// <summary>
///     macOS app discovery service that scans .app bundles in /Applications and ~/Applications.
///     Parses Info.plist for CFBundleDisplayName, CFBundleExecutable, CFBundleIconFile, and CFBundleIdentifier.
///     Maintains a 10-minute cached index with thread-safe locking.
///     Running app enumeration via NSWorkspace is documented but requires Xamarin.Mac SDK at runtime.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacOsAppDiscoveryService(ILogger<MacOsAppDiscoveryService> logger) : IAppDiscoveryService
{
	private static readonly string[] AppSearchPaths =
	[
		"/Applications",
		"~/Applications"
	];

	private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

	private readonly List<AppInfo> _cachedIndex = [];
	private readonly Lock _lock = new();
	private DateTime _lastIndexTime = DateTime.MinValue;

	// ── NSWorkspace P/Invoke (running app enumeration) ──────────────
	// NSWorkspace.runningApplications requires AppKit.framework.
	// On .NET 10 without Xamarin.Mac, we use libproc as fallback for
	// running process detection, similar to MacOSProcessBlocker.

	private const string LibProc = "/usr/lib/libproc.dylib";

	[DllImport(LibProc, SetLastError = true)]
	private static extern int proc_listpids(uint type, uint typeinfo, int[] buffer, int buffersize);

	[DllImport(LibProc, SetLastError = true)]
	private static extern int proc_pidinfo(int pid, int flavor, ulong arg, IntPtr buffer, int buffersize);

	[DllImport(LibProc, SetLastError = true)]
	private static extern int proc_pidpath(int pid, IntPtr buffer, uint buffersize);

	private const uint ProcAllPids = 1;
	private const int ProcPidTbsdinfo = 4;

	public string? FindKnownApp(params string[] processNames)
	{
		var index = GetInstalledApplicationsIndex();

		foreach (var name in processNames)
		{
			// Match by executable filename (case-insensitive)
			var match = index.FirstOrDefault(a =>
				Path.GetFileName(a.ExecutablePath).Equals(name, StringComparison.OrdinalIgnoreCase) ||
				a.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

			if (match != null)
			{
				return match.ExecutablePath;
			}
		}

        // Fallback: check running processes for bundle executable name
        return OperatingSystem.IsMacOS() ? processNames.Select(FindRunningAppBundle).OfType<string>().FirstOrDefault() : null;
    }

	public List<AppInfo> FindAppsByPublisher(string publisherName)
	{
		return [.. GetInstalledApplicationsIndex().Where(a => a.Name.Contains(publisherName, StringComparison.OrdinalIgnoreCase))];
	}

	public List<AppInfo> GetInstalledApplicationsIndex()
	{
		lock (_lock)
		{
			if (_cachedIndex.Count > 0 && DateTime.UtcNow - _lastIndexTime < CacheDuration)
			{
				return [.. _cachedIndex];
			}

			_cachedIndex.Clear();
			var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var searchPath in AppSearchPaths)
			{
				var expandedPath = ExpandPath(searchPath);
				if (!Directory.Exists(expandedPath))
				{
					logger.LogDebug("App search path does not exist: {Path}", expandedPath);
					continue;
				}

				try
				{
					ScanAppBundleDirectory(expandedPath, uniquePaths);
				}
				catch (Exception ex)
				{
					logger.LogWarning(ex, "Failed to scan app directory: {Path}", expandedPath);
				}
			}

			// Sort by name for consistent output
			_cachedIndex.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

			_lastIndexTime = DateTime.UtcNow;
			logger.LogInformation("App index built: {Count} applications found", _cachedIndex.Count);

			return [.. _cachedIndex];
		}
	}

	private void ScanAppBundleDirectory(string directory, HashSet<string> uniquePaths)
	{
		foreach (var appBundle in Directory.EnumerateDirectories(directory, "*.app", SearchOption.TopDirectoryOnly))
		{
			try
			{
				var appInfo = ParseAppBundle(appBundle);
				if (appInfo == null)
				{
					continue;
				}

				if (string.IsNullOrWhiteSpace(appInfo.ExecutablePath))
				{
					continue;
				}

				if (uniquePaths.Add(appInfo.ExecutablePath))
				{
					_cachedIndex.Add(appInfo);
				}
			}
			catch (Exception ex)
			{
				logger.LogDebug(ex, "Failed to parse app bundle: {Bundle}", appBundle);
			}
		}

		// Also scan one level deeper for apps in subdirectories (e.g., /Applications/Utilities/)
		foreach (var subDir in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
		{
			// Skip .app bundles (already handled above)
			if (subDir.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			// Only scan one level deeper to avoid excessive recursion
			try
			{
				foreach (var nestedBundle in Directory.EnumerateDirectories(subDir, "*.app", SearchOption.TopDirectoryOnly))
				{
					try
					{
						var appInfo = ParseAppBundle(nestedBundle);
						if (appInfo == null)
						{
							continue;
						}

						if (string.IsNullOrWhiteSpace(appInfo.ExecutablePath))
						{
							continue;
						}

						if (uniquePaths.Add(appInfo.ExecutablePath))
						{
							_cachedIndex.Add(appInfo);
						}
					}
					catch (Exception ex)
					{
						logger.LogDebug(ex, "Failed to parse nested app bundle: {Bundle}", nestedBundle);
					}
				}
			}
			catch (Exception ex)
			{
				logger.LogDebug(ex, "Failed to scan subdirectory: {Dir}", subDir);
			}
		}
	}

	/// <summary>
	///     Parses a .app bundle's Info.plist to extract app metadata.
	/// </summary>
	private static AppInfo? ParseAppBundle(string bundlePath)
	{
		var plistPath = Path.Combine(bundlePath, "Contents", "Info.plist");
		if (!File.Exists(plistPath))
		{
			return null;
		}

		var plist = ParsePlist(plistPath);
		if (plist == null)
		{
			return null;
		}

		// Extract CFBundleDisplayName (localized), fallback to CFBundleName
		var displayName = plist.GetValueOrDefault("CFBundleDisplayName") as string;
		var bundleName = plist.GetValueOrDefault("CFBundleName") as string;
		var name = !string.IsNullOrWhiteSpace(displayName) ? displayName :
			!string.IsNullOrWhiteSpace(bundleName) ? bundleName :
			Path.GetFileNameWithoutExtension(bundlePath);

		// Extract CFBundleExecutable → resolve to Contents/MacOS/{name}
		var executableName = plist.GetValueOrDefault("CFBundleExecutable") as string;
		string executablePath;
		if (!string.IsNullOrWhiteSpace(executableName))
		{
			executablePath = Path.Combine(bundlePath, "Contents", "MacOS", executableName);
		}
		else
		{
			// Fallback: look for any executable in Contents/MacOS/
			var macosDir = Path.Combine(bundlePath, "Contents", "MacOS");
			if (Directory.Exists(macosDir))
			{
				var firstExe = Directory.EnumerateFiles(macosDir).FirstOrDefault();
				executablePath = firstExe ?? string.Empty;
			}
			else
			{
				executablePath = string.Empty;
			}
		}

		// Extract CFBundleIconFile → resolve to Contents/Resources/{name}.icns
		var iconFile = plist.GetValueOrDefault("CFBundleIconFile") as string;
		string iconPath;
		if (!string.IsNullOrWhiteSpace(iconFile))
		{
			// Icon file might or might not have .icns extension
			var iconName = iconFile.EndsWith(".icns", StringComparison.OrdinalIgnoreCase)
				? iconFile
				: $"{iconFile}.icns";
			iconPath = Path.Combine(bundlePath, "Contents", "Resources", iconName);

			// Verify icon exists
			if (!File.Exists(iconPath))
			{
				iconPath = string.Empty;
			}
		}
		else
		{
			iconPath = string.Empty;
		}

		// Validate executable exists (on macOS, or skip validation on other platforms)
		if (!string.IsNullOrWhiteSpace(executablePath) && OperatingSystem.IsMacOS())
		{
			if (!File.Exists(executablePath))
			{
				return null;
			}
		}

		return new AppInfo(name, executablePath, iconPath);
	}

	/// <summary>
	///     Parses an Info.plist XML file into a dictionary of key-value pairs.
	/// </summary>
	private static Dictionary<string, object?>? ParsePlist(string plistPath)
	{
		try
		{
			var doc = new XmlDocument();
			doc.Load(plistPath);

			// Info.plist structure: <plist><dict><key>...</key><value>...</value></dict></plist>
			var dictNode = doc.SelectSingleNode("//dict");
			if (dictNode == null)
			{
				return null;
			}

			var result = new Dictionary<string, object?>();
			var children = dictNode.ChildNodes;
			string? currentKey = null;

			for (var i = 0; i < children.Count; i++)
			{
				var node = children[i];
				if (node == null)
				{
					continue;
				}

				if (node.Name == "key")
				{
					currentKey = node.InnerText;
				}
				else if (currentKey != null)
				{
					result[currentKey] = node.Name switch
					{
						"string" => node.InnerText,
						"true" => true,
						"false" => false,
						"integer" => int.TryParse(node.InnerText, out var intVal) ? intVal : null,
						"real" => double.TryParse(node.InnerText, out var dblVal) ? dblVal : null,
						_ => node.InnerText
					};
					currentKey = null;
				}
			}

			return result;
		}
		catch (Exception)
		{
			return null;
		}
	}
    
	/// <summary>
	///     Attempts to find a running application by process name using libproc.
	///     This is a fallback when NSWorkspace (AppKit) is unavailable.
	/// </summary>
	private string? FindRunningAppBundle(string processName)
	{
		try
		{
			var pids = GetAllPids();
			if (pids.Length == 0)
			{
				return null;
			}

			foreach (var pid in pids)
			{
				if (pid < 1)
				{
					continue;
				}

				var procName = GetProcessName(pid, IntPtr.Zero);
				if (string.IsNullOrEmpty(procName))
				{
					continue;
				}

				if (procName.Equals(processName, StringComparison.OrdinalIgnoreCase))
				{
					// Found a matching process - try to resolve its bundle
					return ResolveAppBundleFromPid(pid);
				}
			}
		}
		catch (Exception ex)
		{
			logger.LogDebug(ex, "Failed to enumerate running processes for {Name}", processName);
		}

		return null;
	}

	private static string? ResolveAppBundleFromPid(int pid)
	{
		try
		{
			const int maxPath = 1024;
			var buffer = Marshal.AllocHGlobal(maxPath);
			try
			{
				var bytesWritten = proc_pidpath(pid, buffer, maxPath);
				if (bytesWritten <= 0)
				{
					return null;
				}

				var exePath = Marshal.PtrToStringAnsi(buffer, bytesWritten);
				if (string.IsNullOrEmpty(exePath))
				{
					return null;
				}

				// Walk up to find .app bundle
				var current = exePath;
				while (current != null && !current.EndsWith(".app"))
				{
					current = Path.GetDirectoryName(current);
				}

				return current;
			}
			finally
			{
				Marshal.FreeHGlobal(buffer);
			}
		}
		catch
		{
			// Ignore - this is a best-effort resolution
		}

		return null;
	}

	private static int[] GetAllPids()
	{
		var bufferSize = 4096;
		var pids = new int[bufferSize];

		var count = proc_listpids(ProcAllPids, 0, pids, pids.Length * sizeof(int));
		if (count <= 0)
		{
			return [];
		}

		var pidCount = count / sizeof(int);
		if (pidCount > bufferSize)
		{
			pids = new int[pidCount];
			count = proc_listpids(ProcAllPids, 0, pids, pids.Length * sizeof(int));
			pidCount = count > 0 ? count / sizeof(int) : 0;
		}

		return pids[..pidCount];
	}

	private static string? GetProcessName(int pid, IntPtr buffer)
	{
		const int maxPath = 1024;
		var pathBuffer = Marshal.AllocHGlobal(maxPath);
		try
		{
			var bytesWritten = proc_pidpath(pid, pathBuffer, maxPath);
			if (bytesWritten <= 0)
			{
				return null;
			}

			var fullPath = Marshal.PtrToStringAnsi(pathBuffer, bytesWritten);
			if (string.IsNullOrEmpty(fullPath))
			{
				return null;
			}

			return Path.GetFileNameWithoutExtension(fullPath);
		}
		finally
		{
			Marshal.FreeHGlobal(pathBuffer);
		}
	}

	private static string ExpandPath(string path)
	{
		if (path.StartsWith('~'))
		{
			var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			path = Path.Combine(home, path[2..]);
		}

		return Environment.ExpandEnvironmentVariables(path);
	}
}
