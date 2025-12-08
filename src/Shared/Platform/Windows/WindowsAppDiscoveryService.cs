using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Axorith.Shared.Platform.Windows;

[SupportedOSPlatform("windows")]
public class WindowsAppDiscoveryService(
    ILogger<WindowsAppDiscoveryService> logger,
    IEnumerable<string>? fallbackSearchRoots = null)
    : IAppDiscoveryService
{
    private readonly List<AppInfo> _cachedIndex = [];
    private readonly Dictionary<string, CachedPath> _fallbackCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(10);
    private readonly TimeSpan _fallbackCacheDuration = TimeSpan.FromMinutes(30);
    private readonly Lock _lock = new();
    private DateTime _lastIndexTime = DateTime.MinValue;

    private readonly record struct CachedPath(string Path, DateTime Timestamp);

    public string? FindKnownApp(params string[] processNames)
    {
        foreach (var name in processNames)
        {
            var exeName = EnsureExeName(name);

            if (TryGetCachedFallback(exeName, out var cached))
            {
                return cached;
            }

            var registryPath = GetPathFromRegistry(exeName);
            if (!string.IsNullOrEmpty(registryPath) && File.Exists(registryPath))
            {
                RememberFoundPath(exeName, registryPath, name);
                return registryPath;
            }

            var index = GetInstalledApplicationsIndex();
            var match = index.FirstOrDefault(a =>
                Path.GetFileName(a.ExecutablePath).Equals(exeName, StringComparison.OrdinalIgnoreCase) ||
                a.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return match.ExecutablePath;
            }

            var fallback = FindExecutableOnFileSystem(exeName);
            if (!string.IsNullOrEmpty(fallback))
            {
                RememberFoundPath(exeName, fallback, name);
                return fallback;
            }
        }

        return null;
    }

    public List<AppInfo> FindAppsByPublisher(string publisherName)
    {
        var allApps = GetInstalledApplicationsIndex();
        var results = new List<AppInfo>();

        foreach (var app in allApps)
        {
            try
            {
                if (!File.Exists(app.ExecutablePath))
                {
                    continue;
                }

                var info = FileVersionInfo.GetVersionInfo(app.ExecutablePath);
                if (!string.IsNullOrWhiteSpace(info.CompanyName) &&
                    info.CompanyName.Contains(publisherName, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(app);
                }
            }
            catch
            {
                // ignored
            }
        }

        return results;
    }

    public List<AppInfo> GetInstalledApplicationsIndex()
    {
        lock (_lock)
        {
            if (_cachedIndex.Count > 0 && DateTime.UtcNow - _lastIndexTime < _cacheDuration)
            {
                return [.. _cachedIndex];
            }

            _cachedIndex.Clear();
            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddApp(string? name, string path, string? iconPath = null)
            {
                var normalized = NormalizeExecutablePath(path);
                if (string.IsNullOrWhiteSpace(normalized) ||
                    !normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(normalized))
                {
                    return;
                }

                if (uniquePaths.Add(normalized))
                {
                    var finalName = string.IsNullOrWhiteSpace(name)
                        ? Path.GetFileNameWithoutExtension(normalized)
                        : name.Trim();

                    var icon = NormalizeExecutablePath(iconPath) ?? normalized;
                    _cachedIndex.Add(new AppInfo(finalName, normalized, icon));
                }
            }

            ScanStartMenu(Environment.SpecialFolder.CommonStartMenu, AddApp);
            ScanStartMenu(Environment.SpecialFolder.StartMenu, AddApp);
            ScanUninstallRegistry(AddApp);
            ScanKnownInstallFolders(AddApp);
            ScanSteamLibraries(AddApp);

            _lastIndexTime = DateTime.UtcNow;
            return [.. _cachedIndex];
        }
    }

    private void ScanStartMenu(Environment.SpecialFolder folder, Action<string?, string, string?> onFound)
    {
        try
        {
            var root = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return;
            }

            var shortcuts = SafeEnumerateFiles(root, "*.lnk");

            foreach (var shortcut in shortcuts)
            {
                try
                {
                    var target = ResolveShortcut(shortcut);
                    if (!string.IsNullOrEmpty(target))
                    {
                        var name = Path.GetFileNameWithoutExtension(shortcut);
                        onFound(name, target, target);
                    }
                }
                catch
                {
                    // ignored
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to scan start menu folder {Folder}", folder);
        }
    }

    private void ScanUninstallRegistry(Action<string?, string, string?> onFound)
    {
        var hives = new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
        var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };

        foreach (var hive in hives)
        {
            foreach (var view in views)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall == null)
                    {
                        continue;
                    }

                    foreach (var subKeyName in uninstall.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = uninstall.OpenSubKey(subKeyName);
                            if (subKey == null)
                            {
                                continue;
                            }

                            var displayName = subKey.GetValue("DisplayName") as string;
                            var displayIcon = NormalizeExecutablePath(subKey.GetValue("DisplayIcon") as string);
                            var installLocation = subKey.GetValue("InstallLocation") as string;

                            if (!string.IsNullOrEmpty(displayIcon))
                            {
                                onFound(displayName ?? Path.GetFileNameWithoutExtension(displayIcon), displayIcon,
                                    displayIcon);
                            }

                            var candidate = TryFindExeInInstallLocation(displayName, installLocation);
                            if (!string.IsNullOrEmpty(candidate))
                            {
                                onFound(displayName, candidate, displayIcon);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug(ex, "Failed to read uninstall entry {Entry}", subKeyName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to enumerate uninstall keys for {Hive} {View}", hive, view);
                }
            }
        }
    }

    private void ScanKnownInstallFolders(Action<string?, string, string?> onFound)
    {
        var targets = new List<(string Path, int Depth)>
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), 3),
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), 3),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), 2),
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 2),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 2)
        };

        foreach (var (path, depth) in targets)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                continue;
            }

            foreach (var exe in SafeEnumerateFiles(path, "*.exe", depth))
            {
                var name = Path.GetFileNameWithoutExtension(exe);
                onFound(name, exe, exe);
            }
        }
    }

    private void ScanSteamLibraries(Action<string?, string, string?> onFound)
    {
        foreach (var commonDir in GetSteamLibraryRoots())
        {
            try
            {
                if (!Directory.Exists(commonDir))
                {
                    continue;
                }

                foreach (var gameDir in Directory.GetDirectories(commonDir))
                {
                    var gameName = Path.GetFileName(gameDir);
                    foreach (var exe in SafeEnumerateFiles(gameDir, "*.exe", maxDepth: 6))
                    {
                        onFound(gameName, exe, exe);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to scan Steam library {Library}", commonDir);
            }
        }
    }

    private IEnumerable<string> GetSteamLibraryRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var potentialVdfs = new List<string?>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps",
                "libraryfolders.vdf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps",
                "libraryfolders.vdf")
        };

        var registrySteamPath = GetSteamPathFromRegistry();
        if (!string.IsNullOrWhiteSpace(registrySteamPath))
        {
            potentialVdfs.Add(Path.Combine(registrySteamPath, "steamapps", "libraryfolders.vdf"));
            var common = Path.Combine(registrySteamPath, "steamapps", "common");
            if (Directory.Exists(common))
            {
                roots.Add(common);
            }
        }

        foreach (var vdfPath in potentialVdfs.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            if (string.IsNullOrWhiteSpace(vdfPath) || !File.Exists(vdfPath))
            {
                continue;
            }

            foreach (var lib in ParseSteamLibraries(vdfPath))
            {
                var common = Path.Combine(lib, "steamapps", "common");
                if (Directory.Exists(common))
                {
                    roots.Add(common);
                }
            }
        }

        return roots;
    }

    private static IEnumerable<string> ParseSteamLibraries(string vdfPath)
    {
        string contents;
        try
        {
            contents = File.ReadAllText(vdfPath);
        }
        catch
        {
            yield break;
        }

        var matches = Regex.Matches(contents, "\"path\"\\s+\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase);
        foreach (Match match in matches)
        {
            var value = match.Groups["path"].Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var normalized = value.Replace(@"\\", "\\");
            yield return normalized;
        }
    }

    private static string? GetSteamPathFromRegistry()
    {
        try
        {
            using var steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var raw = steamKey?.GetValue("SteamPath") as string;
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch
        {
            return null;
        }
    }

    private string? FindExecutableOnFileSystem(string exeName)
    {
        foreach (var root in GetFallbackRoots())
        {
            try
            {
                var found = FindExecutableInDirectory(root, exeName, maxDepth: 10);
                if (!string.IsNullOrEmpty(found))
                {
                    return found;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed searching for {Exe} in {Root}", exeName, root);
            }
        }

        return null;
    }

    private IEnumerable<string> GetFallbackRoots()
    {
        if (fallbackSearchRoots != null)
        {
            foreach (var root in fallbackSearchRoots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    continue;
                }

                yield return root;
            }

            yield break;
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            string? root = null;

            try
            {
                if (drive is { DriveType: DriveType.Fixed, IsReady: true })
                {
                    root = drive.RootDirectory.FullName;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Skipping drive {Drive}", drive.Name);
            }

            if (!string.IsNullOrEmpty(root))
            {
                yield return root;
            }
        }
    }

    private string? FindExecutableInDirectory(string rootPath, string exeName, int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return null;
        }

        foreach (var file in SafeEnumerateFiles(rootPath, exeName, maxDepth))
        {
            return file;
        }

        return null;
    }

    private bool TryGetCachedFallback(string exeName, out string path)
    {
        lock (_lock)
        {
            if (_fallbackCache.TryGetValue(exeName, out var cached))
            {
                if (DateTime.UtcNow - cached.Timestamp < _fallbackCacheDuration && File.Exists(cached.Path))
                {
                    path = cached.Path;
                    return true;
                }

                _fallbackCache.Remove(exeName);
            }
        }

        path = string.Empty;
        return false;
    }

    private void RememberFoundPath(string exeName, string path, string? displayName)
    {
        lock (_lock)
        {
            _fallbackCache[exeName] = new CachedPath(path, DateTime.UtcNow);

            if (_cachedIndex.All(a => !a.ExecutablePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                var name = string.IsNullOrWhiteSpace(displayName)
                    ? Path.GetFileNameWithoutExtension(path)
                    : displayName;

                _cachedIndex.Add(new AppInfo(name, path, path));
            }
        }
    }

    private static string EnsureExeName(string name)
    {
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.exe";
    }

    private static string? NormalizeExecutablePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var trimmed = rawPath.Trim().Trim('"');
        var commaIndex = trimmed.IndexOf(',');
        if (commaIndex > 0)
        {
            trimmed = trimmed[..commaIndex];
        }

        var exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            trimmed = trimmed[..(exeIndex + 4)];
        }

        try
        {
            trimmed = Environment.ExpandEnvironmentVariables(trimmed);
            trimmed = Path.GetFullPath(trimmed);
        }
        catch
        {
            // ignore normalization issues
        }

        return trimmed;
    }

    private static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private string? TryFindExeInInstallLocation(string? displayName, string? installLocation)
    {
        if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
        {
            return null;
        }

        var preferredName = NormalizeName(displayName);
        string? firstExe = null;

        foreach (var exe in SafeEnumerateFiles(installLocation, "*.exe", maxDepth: 2))
        {
            firstExe ??= exe;

            if (string.IsNullOrWhiteSpace(preferredName))
            {
                continue;
            }

            var current = NormalizeName(Path.GetFileNameWithoutExtension(exe));
            if (current.Contains(preferredName, StringComparison.OrdinalIgnoreCase))
            {
                return exe;
            }
        }

        return firstExe;
    }

    private IEnumerable<string> SafeEnumerateFiles(string rootPath, string searchPattern, int? maxDepth = null)
    {
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((rootPath, 0));

        while (stack.Count > 0)
        {
            var (dir, depth) = stack.Pop();
            string[]? files = null;

            try
            {
                files = Directory.GetFiles(dir, searchPattern);
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore permission errors
            }
            catch (Exception)
            {
                // Ignore other access errors
            }

            if (files != null)
            {
                foreach (var file in files)
                {
                    yield return file;
                }
            }

            if (maxDepth.HasValue && depth >= maxDepth.Value)
            {
                continue;
            }

            string[]? subDirs = null;
            try
            {
                subDirs = Directory.GetDirectories(dir);
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore permission errors
            }
            catch (Exception)
            {
                // Ignore other access errors
            }

            if (subDirs != null)
            {
                foreach (var subDir in subDirs)
                {
                    stack.Push((subDir, depth + 1));
                }
            }
        }
    }

    private static string? GetPathFromRegistry(string exeName)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(Path.Combine(keyPath, exeName));
            var path = key?.GetValue(null) as string; // Default value

            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                return path;
            }

            using var userKey = Registry.CurrentUser.OpenSubKey(Path.Combine(keyPath, exeName));
            var userPath = userKey?.GetValue(null) as string;

            if (!string.IsNullOrEmpty(userPath) && File.Exists(userPath))
            {
                return userPath;
            }
        }
        catch
        {
            // ignored
        }

        return null;
    }

    private static string? ResolveShortcut(string shortcutPath)
    {
        var link = (IShellLink)new ShellLink();

        try
        {
            ((IPersistFile)link).Load(shortcutPath, 0);

            var sb = new StringBuilder(260);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);

            return sb.ToString();
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLink
    {
        void GetPath([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);

        void GetIconLocation([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch,
            out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    internal interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}