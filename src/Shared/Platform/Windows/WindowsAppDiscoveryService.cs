using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Axorith.Shared.Platform.Windows;

[SupportedOSPlatform("windows")]
public class WindowsAppDiscoveryService(ILogger<WindowsAppDiscoveryService> logger) : IAppDiscoveryService
{
    private readonly List<AppInfo> _cachedIndex = [];
    private DateTime _lastIndexTime = DateTime.MinValue;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(10);
    private readonly Lock _lock = new();

    public string? FindKnownApp(params string[] processNames)
    {
        foreach (var name in processNames)
        {
            var exeName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.exe";

            var registryPath = GetPathFromRegistry(exeName);
            if (!string.IsNullOrEmpty(registryPath) && File.Exists(registryPath))
            {
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

            void AddApp(string name, string path)
            {
                if (string.IsNullOrWhiteSpace(path) ||
                    !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(path))
                {
                    return;
                }

                if (uniquePaths.Add(path))
                {
                    _cachedIndex.Add(new AppInfo(name, path, path));
                }
            }

            ScanStartMenu(Environment.SpecialFolder.CommonStartMenu, AddApp);
            ScanStartMenu(Environment.SpecialFolder.StartMenu, AddApp);

            _lastIndexTime = DateTime.UtcNow;
            return [.. _cachedIndex];
        }
    }

    private void ScanStartMenu(Environment.SpecialFolder folder, Action<string, string> onFound)
    {
        try
        {
            var root = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return;
            }

            var shortcuts = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);

            foreach (var shortcut in shortcuts)
            {
                try
                {
                    var target = ResolveShortcut(shortcut);
                    if (!string.IsNullOrEmpty(target))
                    {
                        var name = Path.GetFileNameWithoutExtension(shortcut);
                        onFound(name, target);
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