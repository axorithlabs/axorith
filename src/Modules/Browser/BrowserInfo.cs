namespace Axorith.Module.Browser;

/// <summary>
/// Browser metadata for command-line argument generation.
/// </summary>
internal static class BrowserProfiles
{
    /// <summary>
    /// Known browser profiles with their command-line argument patterns.
    /// Key: lowercase executable name without extension.
    /// </summary>
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
        ["slimbrowser"] = new BrowserProfile(null, null),
    };

    /// <summary>
    /// Gets browser profile by executable name.
    /// </summary>
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

/// <summary>
/// Browser profile with command-line argument patterns.
/// </summary>
/// <param name="ProfileArgument">Format string for profile selection (null if not supported).</param>
/// <param name="IncognitoArgument">Argument for private/incognito mode (null if not supported).</param>
internal sealed record BrowserProfile(string? ProfileArgument, string? IncognitoArgument);
