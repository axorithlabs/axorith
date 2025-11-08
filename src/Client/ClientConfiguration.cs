namespace Axorith.Client;

/// <summary>
///     Configuration model for Axorith.Client.
/// </summary>
public class ClientConfiguration
{
    public HostConnectionConfiguration Host { get; set; } = new();
}

/// <summary>
///     Host connection configuration.
/// </summary>
public class HostConnectionConfiguration
{
    /// <summary>
    ///     Host address (IP or hostname).
    ///     Default: 127.0.0.1 (local)
    /// </summary>
    public string Address { get; set; } = "127.0.0.1";

    /// <summary>
    ///     Host gRPC port.
    ///     Default: 5901
    /// </summary>
    public int Port { get; set; } = 5901;

    /// <summary>
    ///     Connection timeout in seconds.
    ///     Default: 10
    /// </summary>
    public int ConnectionTimeout { get; set; } = 10;

    /// <summary>
    ///     Health check interval in seconds.
    ///     Default: 5
    /// </summary>
    public int HealthCheckInterval { get; set; } = 5;

    /// <summary>
    ///     Auto-start local host if not running.
    ///     Only applicable when UseRemoteHost = false.
    ///     Default: true
    /// </summary>
    public bool AutoStartHost { get; set; } = true;

    /// <summary>
    ///     Use remote host instead of local.
    ///     If true, client connects to remote Address:Port without starting local host.
    ///     Default: false (local mode)
    /// </summary>
    public bool UseRemoteHost { get; set; }

    /// <summary>
    ///     Gets the full gRPC endpoint URL.
    /// </summary>
    public string GetEndpointUrl()
    {
        return $"http://{Address}:{Port}";
    }
}