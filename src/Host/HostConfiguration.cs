namespace Axorith.Host;

/// <summary>
///     Configuration model for Axorith.Host.
/// </summary>
public class HostConfiguration
{
    public GrpcConfiguration Grpc { get; set; } = new();
    public ModulesConfiguration Modules { get; set; } = new();
    public PersistenceConfiguration Persistence { get; set; } = new();
    public SessionConfiguration Session { get; set; } = new();
}

public class GrpcConfiguration
{
    public int Port { get; set; } = 5901;
    public string BindAddress { get; set; } = "127.0.0.1";
    public int MaxConcurrentStreams { get; set; } = 100;
    public int KeepAliveInterval { get; set; } = 30;
    public int KeepAliveTimeout { get; set; } = 10;
}

public class ModulesConfiguration
{
    public List<string> SearchPaths { get; set; } = [];
    public bool EnableHotReload { get; set; }
    
    /// <summary>
    ///     Whitelist of allowed symlink paths for development.
    ///     Only these symlinked directories will be scanned for modules.
    ///     Empty list means no symlinks are allowed (production default).
    /// </summary>
    public List<string> AllowedSymlinks { get; set; } = [];

    /// <summary>
    ///     Resolves environment variables in all search paths and returns expanded paths.
    ///     Provides fallback to default paths if configuration is empty.
    /// </summary>
    public IEnumerable<string> ResolveSearchPaths()
    {
        if (SearchPaths.Count == 0)
        {
            // Fallback to default paths if not configured
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Axorith", "modules");
            var devPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../modules"));
            return [appDataPath, devPath];
        }

        return SearchPaths
            .Select(Environment.ExpandEnvironmentVariables)
            .Select(Path.GetFullPath)  // Normalize path (handle ../ and mixed slashes)
            .ToList();
    }
}

public class PersistenceConfiguration
{
    public string PresetsPath { get; set; } = string.Empty;
    public string LogsPath { get; set; } = string.Empty;

    /// <summary>
    ///     Resolves environment variables in paths (e.g., %AppData%).
    ///     Provides fallback to default path if not configured.
    /// </summary>
    public string ResolvePresetsPath()
    {
        if (string.IsNullOrWhiteSpace(PresetsPath))
        {
            // Fallback to default if not configured
            var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appDataFolder, "Axorith", "presets");
        }

        var expandedPath = Environment.ExpandEnvironmentVariables(PresetsPath);
        return Path.GetFullPath(expandedPath);  // Normalize path
    }

    /// <summary>
    ///     Resolves environment variables in paths.
    ///     Provides fallback to default path if not configured.
    /// </summary>
    public string ResolveLogsPath()
    {
        if (string.IsNullOrWhiteSpace(LogsPath))
        {
            // Fallback to default if not configured
            var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appDataFolder, "Axorith", "logs");
        }

        var expandedPath = Environment.ExpandEnvironmentVariables(LogsPath);
        return Path.GetFullPath(expandedPath);  // Normalize path
    }
}

public class SessionConfiguration
{
    /// <summary>
    ///     Timeout in seconds for module settings validation during session startup.
    ///     Default: 5 seconds.
    /// </summary>
    public int ValidationTimeoutSeconds { get; set; } = 5;

    /// <summary>
    ///     Timeout in seconds for module startup (OnSessionStartAsync) during session initialization.
    ///     Increase this for modules with slow initialization (e.g., OAuth login).
    ///     Default: 30 seconds.
    /// </summary>
    public int StartupTimeoutSeconds { get; set; } = 30;

    /// <summary>
    ///     Timeout in seconds for module cleanup (OnSessionEndAsync) during session shutdown.
    ///     Default: 10 seconds.
    /// </summary>
    public int ShutdownTimeoutSeconds { get; set; } = 10;
}