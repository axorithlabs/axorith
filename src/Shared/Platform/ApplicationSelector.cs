namespace Axorith.Shared.Platform;

public static class ApplicationSelector
{
    private static readonly HashSet<string> SupportedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome.exe", "firefox.exe", "msedge.exe", "brave.exe", "opera.exe", "vivaldi.exe",
        "chromium.exe", "waterfox.exe", "librewolf.exe", "floorp.exe", "zen.exe", "arc.exe",
        "palemoon.exe", "tor.exe", "obs64.exe", "obs32.exe", "discord.exe", "code.exe",
        "steam.exe", "spotify.exe"
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

    public static bool IsSupportedLauncherApp(AppInfo app)
    {
        var executable = Path.GetFileName(app.ExecutablePath).ToLowerInvariant();
        return SupportedExecutables.Contains(executable) ||
               IsJetBrainsExecutable(executable);
    }

    public static string? GetLauncherModuleKey(string applicationPath)
    {
        var executable = Path.GetFileName(applicationPath).ToLowerInvariant();
        if (SupportedExecutables.Contains(executable))
        {
            if (executable is "chrome.exe" or "firefox.exe" or "msedge.exe" or "brave.exe" or "opera.exe" or
                "vivaldi.exe" or "chromium.exe" or "waterfox.exe" or "librewolf.exe" or "floorp.exe" or
                "zen.exe" or "arc.exe" or "palemoon.exe" or "tor.exe")
                return "Browser";
            return executable switch
            {
                "obs64.exe" or "obs32.exe" => "OBS",
                "discord.exe" => "Discord",
                "code.exe" => "VSCode",
                "steam.exe" => "Steam",
                "spotify.exe" => "Spotify",
                _ => null
            };
        }

        if (IsJetBrainsExecutable(executable))
            return "JetBrainsIDE";

        return null;
    }

    private static bool IsJetBrainsExecutable(string executable) => executable is
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
