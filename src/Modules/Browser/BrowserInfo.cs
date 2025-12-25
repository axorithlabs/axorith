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
        ["chrome"] = new("--profile-directory=\"{0}\"", "--incognito"),
        ["chromium"] = new("--profile-directory=\"{0}\"", "--incognito"),
        ["msedge"] = new("--profile-directory=\"{0}\"", "--inprivate"),
        ["brave"] = new("--profile-directory=\"{0}\"", "--incognito"),
        ["vivaldi"] = new("--profile-directory=\"{0}\"", "--incognito"),
        ["opera"] = new(null, "--private"),
        ["browser"] = new("--profile-directory=\"{0}\"", "--incognito"), // Yandex Browser
        ["arc"] = new(null, null), // Arc Browser
        
        // Firefox-based browsers
        ["firefox"] = new("-P \"{0}\"", "-private-window"),
        ["waterfox"] = new("-P \"{0}\"", "-private-window"),
        ["librewolf"] = new("-P \"{0}\"", "-private-window"),
        ["floorp"] = new("-P \"{0}\"", "-private-window"),
        ["zen"] = new("-P \"{0}\"", "-private-window"),
        ["palemoon"] = new("-P \"{0}\"", "-private-window"),
        
        // Other browsers
        ["safari"] = new(null, null), // Safari (macOS)
        ["iexplore"] = new(null, "-private"), // Internet Explorer
        ["tor"] = new(null, null), // Tor Browser
        ["maxthon"] = new(null, null),
        ["slimbrowser"] = new(null, null),
    };

    /// <summary>
    /// Gets browser profile by executable name.
    /// </summary>
    public static BrowserProfile? GetProfile(string executablePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(executablePath).ToLowerInvariant();
        
        // Try exact match first
        if (KnownBrowsers.TryGetValue(fileName, out var profile))
        {
            return profile;
        }
        
        // Try partial match for variants (e.g., "obs64" -> "obs")
        foreach (var (key, value) in KnownBrowsers)
        {
            if (fileName.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }
        
        // Default to Chrome-like arguments for unknown Chromium-based browsers
        return new BrowserProfile("--profile-directory=\"{0}\"", "--incognito");
    }
}

/// <summary>
/// Browser profile with command-line argument patterns.
/// </summary>
/// <param name="ProfileArgument">Format string for profile selection (null if not supported).</param>
/// <param name="IncognitoArgument">Argument for private/incognito mode (null if not supported).</param>
internal sealed record BrowserProfile(string? ProfileArgument, string? IncognitoArgument);
