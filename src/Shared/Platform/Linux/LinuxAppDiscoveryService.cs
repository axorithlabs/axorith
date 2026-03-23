using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Linux;

[SupportedOSPlatform("linux")]
internal class LinuxAppDiscoveryService(
    ILogger<LinuxAppDiscoveryService> logger,
    IEnumerable<string>? additionalSearchPaths = null) : IAppDiscoveryService
{
    private static readonly string[] DefaultSearchPaths =
    [
        "/usr/share/applications",
        "/var/lib/flatpak/exports/share/applications",
        "~/.local/share/applications"
    ];

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    private readonly List<AppInfo> _cachedIndex = [];
    private readonly object _lock = new();
    private DateTime _lastIndexTime = DateTime.MinValue;

    public string? FindKnownApp(params string[] processNames)
    {
        var index = GetInstalledApplicationsIndex();

        return (from name in processNames let exeName = EnsureExeName(name) select index.FirstOrDefault(a => Path.GetFileName(a.ExecutablePath).Equals(exeName, StringComparison.OrdinalIgnoreCase) || a.Name.Contains(name, StringComparison.OrdinalIgnoreCase))).OfType<AppInfo>().Select(match => match.ExecutablePath).FirstOrDefault();
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

            var searchPaths = additionalSearchPaths?.Select(ExpandPath).ToArray() ?? [];
            var allPaths = DefaultSearchPaths.Select(ExpandPath).Concat(searchPaths).Distinct().ToArray();

            foreach (var baseDir in allPaths)
            {
                if (!Directory.Exists(baseDir))
                {
                    continue;
                }

                try
                {
                    ScanDesktopDirectory(baseDir, uniquePaths);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to scan directory {Dir}", baseDir);
                }
            }

            _lastIndexTime = DateTime.UtcNow;
            return [.. _cachedIndex];
        }
    }

    private void ScanDesktopDirectory(string directory, HashSet<string> uniquePaths)
    {
        var desktopFiles = Directory.EnumerateFiles(directory, "*.desktop", SearchOption.AllDirectories);

        foreach (var file in desktopFiles)
        {
            try
            {
                var appInfo = ParseDesktopFile(file);
                if (appInfo == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(appInfo.ExecutablePath) ||
                    !File.Exists(appInfo.ExecutablePath))
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
                logger.LogDebug(ex, "Failed to parse desktop file {File}", file);
            }
        }
    }

    private static AppInfo? ParseDesktopFile(string filePath)
    {
        var content = File.ReadAllText(filePath);

        if (content.Contains("NoDisplay=true", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("Type=Link", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var nameMatch = Regex.Match(content, "^Name=(.+)$", RegexOptions.Multiline);
        var iconMatch = Regex.Match(content, "^Icon=(.+)$", RegexOptions.Multiline);
        var execMatch = Regex.Match(content, "^Exec=(.+)$", RegexOptions.Multiline);

        if (!execMatch.Success)
        {
            return null;
        }

        var name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(filePath);
        var iconPath = iconMatch.Success ? iconMatch.Groups[1].Value.Trim() : string.Empty;
        var exec = CleanExecCommand(execMatch.Groups[1].Value.Trim());

        if (string.IsNullOrWhiteSpace(exec))
        {
            return null;
        }

        return new AppInfo(name, exec, ResolveIconPath(iconPath));
    }

    private static string CleanExecCommand(string exec)
    {
        exec = Regex.Replace(exec, " %[uUfFdDnNcC]", " ");
        exec = exec.Trim();

        var firstSpace = exec.IndexOf(' ');
        var exePath = firstSpace > 0 ? exec[..firstSpace] : exec;

        exePath = Environment.ExpandEnvironmentVariables(exePath);

        if (exePath.StartsWith('/'))
        {
            return exePath;
        }

        var firstArg = firstSpace > 0 ? exec[(firstSpace + 1)..].TrimStart() : string.Empty;
        return firstSpace > 0 ? $"{exePath} {firstArg}" : exePath;
    }

    private static string ResolveIconPath(string iconValue)
    {
        if (string.IsNullOrWhiteSpace(iconValue))
        {
            return string.Empty;
        }

        if (iconValue.StartsWith('/'))
        {
            return File.Exists(iconValue) ? iconValue : string.Empty;
        }

        var iconName = Path.GetFileNameWithoutExtension(iconValue);
        var iconExt = Path.GetExtension(iconValue);

        var searchDirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "icons"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "icons"),
            "/usr/share/icons/hicolor",
            "/usr/share/pixmaps"
        };

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var ext in new[] { iconExt, ".png", ".svg", ".xpm" })
            {
                var candidate = Path.Combine(dir, $"{iconName}{ext}");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return iconValue;
    }

    private static string ExpandPath(string path)
    {
        if (!path.StartsWith('~'))
        {
            return Environment.ExpandEnvironmentVariables(path);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        path = Path.Combine(home, path[2..]);

        return Environment.ExpandEnvironmentVariables(path);
    }

    private static string EnsureExeName(string name)
    {
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name}.exe";
    }
}
