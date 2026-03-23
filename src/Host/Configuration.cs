using Axorith.Shared.Utils;

namespace Axorith.Host;

public class Configuration
{
    public GrpcConfiguration Grpc { get; init; } = new();
    public ModulesConfiguration Modules { get; init; } = new();
    public PersistenceConfiguration Persistence { get; init; } = new();
    public SessionConfiguration Session { get; init; } = new();
    public DesignTimeConfiguration DesignTime { get; init; } = new();
    public StreamingConfiguration Streaming { get; init; } = new();
    public BrowserExtensionsConfiguration BrowserExtensions { get; init; } = new();
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

	/// <summary>
	///     When true, allows HTTP/2 keep-alive pings even when there are no active RPC calls.
	///     This is essential for presence streaming to detect connection loss promptly.
	/// </summary>
	public bool KeepAlivePermitWithoutCalls { get; init; } = true;

	/// <summary>
	///     IPC endpoint path for local communication.
	///     On Unix: path to Unix Domain Socket file.
	///     On Windows: Named Pipe name.
	///     Empty string uses the default from ApplicationPaths.IpcEndpoint.
	/// </summary>
	public string IpcEndpoint { get; init; } = string.Empty;

	/// <summary>
	///     Resolves the actual IPC endpoint path, using ApplicationPaths default if not configured.
	/// </summary>
	public string ResolveIpcEndpoint()
	{
		return string.IsNullOrWhiteSpace(IpcEndpoint)
			? ApplicationPaths.IpcEndpoint
			: IpcEndpoint;
	}
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

    public IEnumerable<string> ResolveSearchPaths()
    {
        if (SearchPaths.Count == 0)
        {
            var devPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../Modules"));
            return [ApplicationPaths.Modules, devPath];
        }

        return SearchPaths
            .Select(Environment.ExpandEnvironmentVariables)
            .Select(Path.GetFullPath)
            .ToList();
    }
}

public class PersistenceConfiguration
{
    public string PresetsPath { get; init; } = string.Empty;
    public string LogsPath { get; init; } = string.Empty;
    public string ConfigPath { get; init; } = string.Empty;

    public string ResolvePresetsPath()
    {
        if (string.IsNullOrWhiteSpace(PresetsPath))
        {
            return ApplicationPaths.Presets;
        }

        return ApplicationPaths.ExpandPath(PresetsPath);
    }

    public string ResolveLogsPath()
    {
        if (string.IsNullOrWhiteSpace(LogsPath))
        {
            return ApplicationPaths.Logs;
        }

        return ApplicationPaths.ExpandPath(LogsPath);
    }

    public string ResolveConfigPath()
    {
        if (string.IsNullOrWhiteSpace(ConfigPath))
        {
            return ApplicationPaths.Config;
        }

        return ApplicationPaths.ExpandPath(ConfigPath);
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

/// <summary>
///     Browser extension configuration for Native Messaging registration.
/// </summary>
public class BrowserExtensionsConfiguration
{
    /// <summary>
    ///     Chrome/Chromium extension ID for Native Messaging host registration.
    ///     Set this to your actual extension ID before deployment.
    ///     Default: empty (Native Messaging will log an error and skip registration).
    /// </summary>
    public string ChromeExtensionId { get; init; } = string.Empty;

    /// <summary>
    ///     Firefox extension ID for Native Messaging host registration.
    /// </summary>
    public string FirefoxExtensionId { get; init; } = "site-blocker-firefox@axorithlabs.com";
}