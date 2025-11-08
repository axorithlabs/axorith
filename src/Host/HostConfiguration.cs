namespace Axorith.Host;

/// <summary>
///     Configuration model for Axorith.Host.
/// </summary>
public class HostConfiguration
{
    public GrpcConfiguration Grpc { get; set; } = new();
    public ModulesConfiguration Modules { get; set; } = new();
    public PersistenceConfiguration Persistence { get; set; } = new();
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
}

public class PersistenceConfiguration
{
    public string PresetsPath { get; set; } = string.Empty;
    public string LogsPath { get; set; } = string.Empty;

    /// <summary>
    ///     Resolves environment variables in paths (e.g., %AppData%).
    /// </summary>
    public string ResolvePresetsPath()
    {
        return Environment.ExpandEnvironmentVariables(PresetsPath);
    }

    /// <summary>
    ///     Resolves environment variables in paths.
    /// </summary>
    public string ResolveLogsPath()
    {
        return Environment.ExpandEnvironmentVariables(LogsPath);
    }
}