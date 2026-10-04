namespace Axorith.Shared.Platform;

public static class ApplicationSelector
{
    private static readonly Dictionary<string, string> LauncherModules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome.exe"] = "Browser", ["firefox.exe"] = "Browser", ["msedge.exe"] = "Browser",
        ["brave.exe"] = "Browser", ["opera.exe"] = "Browser", ["vivaldi.exe"] = "Browser",
        ["chromium.exe"] = "Browser", ["waterfox.exe"] = "Browser", ["librewolf.exe"] = "Browser",
        ["floorp.exe"] = "Browser", ["zen.exe"] = "Browser", ["arc.exe"] = "Browser",
        ["palemoon.exe"] = "Browser", ["tor.exe"] = "Browser", ["browser.exe"] = "Browser",
        ["safari.exe"] = "Browser", ["maxthon.exe"] = "Browser", ["slimbrowser.exe"] = "Browser",
        ["iexplore.exe"] = "Browser", ["yandex.exe"] = "Browser",
        ["obs64.exe"] = "OBS", ["obs32.exe"] = "OBS", ["discord.exe"] = "Discord",
        ["code.exe"] = "VSCode", ["steam.exe"] = "Steam", ["spotify.exe"] = "Spotify"
    };

    public static List<KeyValuePair<string, string>> GetInstalledChoices(
        IAppDiscoveryService discovery,
        Func<AppInfo, string> keySelector,
        Func<AppInfo, bool>? filter = null) =>
        discovery.GetInstalledApplicationsIndex()
            .Where(app => !string.IsNullOrWhiteSpace(app.ExecutablePath))
            .Where(app => !IsWindowsSystemExecutable(app.ExecutablePath))
            .Where(app => filter?.Invoke(app) ?? true)
            .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .Select(app => new KeyValuePair<string, string>(keySelector(app),
                $"{app.Name}\n{app.ExecutablePath}\n{app.IconPath}"))
            .DistinctBy(choice => Path.GetFileNameWithoutExtension(choice.Key), StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static bool IsSupportedLauncherApp(AppInfo app) =>
        LauncherModules.ContainsKey(Path.GetFileName(app.ExecutablePath)) || IsJetBrainsExecutable(app.ExecutablePath);

    public static bool IsBrowserApp(AppInfo app) =>
        LauncherModules.TryGetValue(Path.GetFileName(app.ExecutablePath), out var module) && module == "Browser";

    public static string? GetLauncherModuleKey(string applicationPath)
    {
        var executable = Path.GetFileName(applicationPath);
        return LauncherModules.TryGetValue(executable, out var module)
            ? module
            : IsJetBrainsExecutable(executable) ? "JetBrainsIDE" : null;
    }

    private static bool IsJetBrainsExecutable(string path) => Path.GetFileName(path).ToLowerInvariant() is
        "idea.exe" or "idea64.exe" or "rider.exe" or "rider64.exe" or
        "webstorm.exe" or "webstorm64.exe" or "pycharm.exe" or "pycharm64.exe" or
        "clion.exe" or "clion64.exe" or "goland.exe" or "goland64.exe" or
        "phpstorm.exe" or "phpstorm64.exe" or "rubymine.exe" or "rubymine64.exe" or
        "datagrip.exe" or "datagrip64.exe";

    private static bool IsWindowsSystemExecutable(string path)
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return !string.IsNullOrWhiteSpace(windowsDirectory) &&
               Path.GetFullPath(path).StartsWith(
                   Path.GetFullPath(windowsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }
}
