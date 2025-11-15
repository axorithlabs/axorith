namespace Axorith.Host;

/// <summary>
///     Configuration model for Axorith.Host.
/// </summary>
public class Configuration
{
    public GrpcConfiguration Grpc { get; init; } = new();
    public ModulesConfiguration Modules { get; init; } = new();
    public PersistenceConfiguration Persistence { get; init; } = new();
    public SessionConfiguration Session { get; init; } = new();
    public DesignTimeConfiguration DesignTime { get; init; } = new();
    public StreamingConfiguration Streaming { get; init; } = new();
}

public class DesignTimeConfiguration
{
    public int SandboxIdleTtlSeconds { get; init; } = 300;
    public int MaxSandboxes { get; init; } = 5;
    public int EvictionIntervalSeconds { get; init; } = 60;
}

public class StreamingConfiguration
{
    public int ChoicesThrottleMs { get; init; } = 200;
    public int ValueBatchWindowMs { get; init; } = 16;
}

public class GrpcConfiguration
{
    public int Port { get; init; } = 5901;
    public string BindAddress { get; init; } = "127.0.0.1";
    public int MaxConcurrentStreams { get; init; } = 100;
    public int KeepAliveInterval { get; init; } = 30;
    public int KeepAliveTimeout { get; init; } = 10;
}

public class ModulesConfiguration
{
    public List<string> SearchPaths { get; init; } = [];
    public bool EnableHotReload { get; init; }

    /// <summary>
    ///     Whitelist of allowed symlink paths for development.
    ///     Only these symlinked directories will be scanned for modules.
    ///     Empty list means no symlinks are allowed (production default).
    /// </summary>
    public List<string> AllowedSymlinks { get; init; } = [];

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
            .Select(Path.GetFullPath) // Normalize path (handle ../ and mixed slashes)
            .ToList();
    }
}

public class PersistenceConfiguration
{
    public string PresetsPath { get; init; } = string.Empty;
    public string LogsPath { get; init; } = string.Empty;

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
        return Path.GetFullPath(expandedPath); // Normalize path
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
        return Path.GetFullPath(expandedPath); // Normalize path
    }
}

public class SessionConfiguration
{
    /// <summary>
    ///     Timeout in seconds for module settings validation during session startup.
    ///     Default: 5 seconds.
    /// </summary>
    public int ValidationTimeoutSeconds { get; init; } = 5;

    /// <summary>
    ///     Timeout in seconds for module startup (OnSessionStartAsync) during session initialization.
    ///     Increase this for modules with slow initialization (e.g., OAuth login).
    ///     Default: 30 seconds.
    /// </summary>
    public int StartupTimeoutSeconds { get; init; } = 30;

    /// <summary>
    ///     Timeout in seconds for module cleanup (OnSessionEndAsync) during session shutdown.
    ///     Default: 10 seconds.
    /// </summary>
    public int ShutdownTimeoutSeconds { get; init; } = 10;
}