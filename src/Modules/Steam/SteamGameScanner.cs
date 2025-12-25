using System.Text.RegularExpressions;

namespace Axorith.Module.Steam;

/// <summary>
/// Scans Steam library folders for installed games.
/// </summary>

internal static class SteamGameScanner
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
    
    private static readonly string[] SkipPatterns = 
    [
        "Redistributable", "DirectX", "Visual C++", "Steamworks", 
        "Proton", "Steam Linux Runtime", "SteamVR"
    ];

    /// <summary>
    /// Gets Steam installation directory from executable path.
    /// </summary>
    public static string? GetSteamDirectory(string steamExePath)
    {
        if (string.IsNullOrEmpty(steamExePath))
            return null;
            
        var dir = Path.GetDirectoryName(steamExePath);
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>
    /// Gets all installed games from Steam directory.
    /// </summary>
    public static List<SteamGame> GetInstalledGames(string steamDirectory)
    {
        var games = new List<SteamGame>();
        var libraries = GetLibraryFolders(steamDirectory);

        foreach (var library in libraries)
        {
            var steamappsPath = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamappsPath))
                continue;

            try
            {
                foreach (var manifestPath in Directory.EnumerateFiles(steamappsPath, "appmanifest_*.acf"))
                {
                    var game = ParseAppManifest(manifestPath);
                    if (game != null && games.All(g => g.AppId != game.AppId))
                    {
                        games.Add(game);
                    }
                }
            }
            catch
            {
                // Skip inaccessible libraries
            }
        }

        return games.OrderBy(g => g.Name).ToList();
    }

    private static List<string> GetLibraryFolders(string steamDirectory)
    {
        var libraries = new List<string> { steamDirectory };

        var libraryFoldersPath = Path.Combine(steamDirectory, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFoldersPath))
            return libraries;

        try
        {
            var content = File.ReadAllText(libraryFoldersPath);
            var matches = Regex.Matches(content, @"""path""\s+""([^""]+)""", 
                RegexOptions.IgnoreCase, RegexTimeout);
            
            foreach (Match match in matches)
            {
                var libPath = match.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(libPath) && !libraries.Contains(libPath, StringComparer.OrdinalIgnoreCase))
                {
                    libraries.Add(libPath);
                }
            }
        }
        catch
        {
            // Return what we have
        }

        return libraries;
    }

    private static SteamGame? ParseAppManifest(string manifestPath)
    {
        try
        {
            var content = File.ReadAllText(manifestPath);

            var appIdMatch = Regex.Match(content, @"""appid""\s+""(\d+)""", RegexOptions.IgnoreCase, RegexTimeout);
            var nameMatch = Regex.Match(content, @"""name""\s+""([^""]+)""", RegexOptions.IgnoreCase, RegexTimeout);

            if (!appIdMatch.Success || !nameMatch.Success)
                return null;

            var name = nameMatch.Groups[1].Value;
            
            // Skip tools and redistributables
            if (SkipPatterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                return null;

            return new SteamGame(appIdMatch.Groups[1].Value, name);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Represents an installed Steam game.
/// </summary>
internal sealed record SteamGame(string AppId, string Name);
