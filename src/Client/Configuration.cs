namespace Axorith.Client;

/// <summary>
///     Configuration model for Axorith.Client.
/// </summary>
public class Configuration
{
    public HostConnectionConfiguration Host { get; set; } = new();
    public ClientUiConfiguration Ui { get; set; } = new();
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

/// <summary>
///     UI-related configuration for Axorith.Client.
/// </summary>
public class ClientUiConfiguration
{
    /// <summary>
    ///     If true, closing the main window will minimize it to the tray instead of exiting.
    ///     Default: true
    /// </summary>
    public bool MinimizeToTrayOnClose { get; set; } = true;

    /// <summary>
    ///     Stores input history for settings with HasHistory enabled (e.g., file/directory pickers).
    ///     Key: Setting.Key, Value: List of recent values.
    /// </summary>
    public Dictionary<string, List<string>> InputHistory { get; set; } = [];

    /// <summary>
    ///     Settings input behavior configuration (debounce, throttle, etc.)
    /// </summary>
    public SettingsInputConfiguration SettingsInput { get; set; } = new();
}

/// <summary>
///     Configuration for settings input behavior (debounce/throttle timings).
/// </summary>
public class SettingsInputConfiguration
{
    private const int DefaultTextDebounceMs = 500;
    private const int MinTextDebounceMs = 100;
    private const int MaxTextDebounceMs = 2000;

    private const int DefaultNumberThrottleMs = 75;
    private const int MinNumberThrottleMs = 0;
    private const int MaxNumberThrottleMs = 500;

    /// <summary>
    ///     Debounce duration in milliseconds for text-based settings (Text, TextArea, FilePicker, DirectoryPicker, Secret).
    ///     Updates are sent only after user stops typing for this duration.
    ///     Default: 500ms. Range: 100-2000ms.
    /// </summary>
    public int TextDebounceMs
    {
        get;
        set => field = Math.Clamp(value, MinTextDebounceMs, MaxTextDebounceMs);
    } = DefaultTextDebounceMs;

    /// <summary>
    ///     Throttle duration in milliseconds for numeric settings.
    ///     Default: 75ms. Range: 0-500ms.
    /// </summary>
    public int NumberThrottleMs
    {
        get;
        set => field = Math.Clamp(value, MinNumberThrottleMs, MaxNumberThrottleMs);
    } = DefaultNumberThrottleMs;

    /// <summary>
    ///     Whether to immediately send pending changes when a text field loses focus.
    ///     Default: true.
    /// </summary>
    public bool FlushOnFocusLoss { get; set; } = true;
}