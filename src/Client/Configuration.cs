namespace Axorith.Client;

public class Configuration
{
    public HostConnectionConfiguration Host { get; set; } = new();
    public ClientUiConfiguration Ui { get; set; } = new();
}

public class HostConnectionConfiguration
{
    public string Address { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 5901;

    public int ConnectionTimeout { get; set; } = 10;

    public int HealthCheckInterval { get; set; } = 5;

    public bool AutoStartHost { get; set; } = true;

    public bool UseRemoteHost { get; set; }

    public string GetEndpointUrl() => $"http://{Address}:{Port}";
}

public class ClientUiConfiguration
{
    public bool MinimizeToTrayOnClose { get; set; } = true;

    public bool TelemetryEnabled { get; set; } = true;

    public bool AutoStartEnabled { get; set; } = true;

    public bool AutoStartMinimized { get; set; } = true;

    public Dictionary<string, List<string>> InputHistory { get; set; } = [];

    public SettingsInputConfiguration SettingsInput { get; set; } = new();
}

public class SettingsInputConfiguration
{
    private const int DefaultTextDebounceMs = 500;
    private const int MinTextDebounceMs = 100;
    private const int MaxTextDebounceMs = 2000;

    private const int DefaultNumberThrottleMs = 75;
    private const int MinNumberThrottleMs = 0;
    private const int MaxNumberThrottleMs = 500;

    public int TextDebounceMs
    {
        get;
        set => field = Math.Clamp(value, MinTextDebounceMs, MaxTextDebounceMs);
    } = DefaultTextDebounceMs;

    public int NumberThrottleMs
    {
        get;
        set => field = Math.Clamp(value, MinNumberThrottleMs, MaxNumberThrottleMs);
    } = DefaultNumberThrottleMs;

    public bool FlushOnFocusLoss { get; set; } = true;
}
