namespace Axorith.Module.ApplicationLauncher.Apps.Browser;

internal static class BrowserProfiles
{
    public static readonly Dictionary<string, BrowserProfile> KnownBrowsers = new(StringComparer.OrdinalIgnoreCase)
    {
        // Chromium-based browsers
        ["chrome"] = new BrowserProfile("--profile-directory=\"{0}\"", "--incognito"),
        ["chromium"] = new BrowserProfile("--profile-directory=\"{0}\"", "--incognito"),
        ["msedge"] = new BrowserProfile("--profile-directory=\"{0}\"", "--inprivate"),
        ["brave"] = new BrowserProfile("--profile-directory=\"{0}\"", "--incognito"),
        ["vivaldi"] = new BrowserProfile("--profile-directory=\"{0}\"", "--incognito"),
        ["opera"] = new BrowserProfile(null, "--private"),
        ["browser"] = new BrowserProfile("--profile-directory=\"{0}\"", "--incognito"), // Yandex Browser
        ["arc"] = new BrowserProfile(null, null), // Arc Browser

        // Firefox-based browsers
        ["firefox"] = new BrowserProfile("-P \"{0}\"", "-private-window"),
        ["waterfox"] = new BrowserProfile("-P \"{0}\"", "-private-window"),
        ["librewolf"] = new BrowserProfile("-P \"{0}\"", "-private-window"),
        ["floorp"] = new BrowserProfile("-P \"{0}\"", "-private-window"),
        ["zen"] = new BrowserProfile("-P \"{0}\"", "-private-window"),
        ["palemoon"] = new BrowserProfile("-P \"{0}\"", "-private-window"),

        // Other browsers
        ["safari"] = new BrowserProfile(null, null), // Safari (macOS)
        ["iexplore"] = new BrowserProfile(null, "-private"), // Internet Explorer
        ["tor"] = new BrowserProfile(null, null), // Tor Browser
        ["maxthon"] = new BrowserProfile(null, null),
        ["slimbrowser"] = new BrowserProfile(null, null)
    };

    public static BrowserProfile? GetProfile(string executablePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(executablePath).ToLowerInvariant();

        if (KnownBrowsers.TryGetValue(fileName, out var profile))
        {
            return profile;
        }

        foreach (var (key, value) in KnownBrowsers)
        {
            if (fileName.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return new BrowserProfile("--profile-directory=\"{0}\"", "--incognito");
    }
}

internal sealed record BrowserProfile(string? ProfileArgument, string? IncognitoArgument);
