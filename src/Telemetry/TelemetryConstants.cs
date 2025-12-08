namespace Axorith.Telemetry;

/// <summary>
///     Constants for telemetry event names and property keys.
///     Eliminates magic strings throughout the telemetry codebase.
/// </summary>
public static class TelemetryConstants
{
    /// <summary>PostHog identify event for user/device identification.</summary>
    public const string IdentifyEvent = "$identify";

    /// <summary>Event name for log entries forwarded to telemetry.</summary>
    public const string LogEvent = "LogEvent";

    /// <summary>Default event name when none is specified.</summary>
    public const string DefaultEvent = "event";

    /// <summary>
    ///     Property name constants used in telemetry payloads.
    /// </summary>
    public static class Properties
    {
        /// <summary>Unique identifier for the user/device.</summary>
        public const string DistinctId = "distinct_id";

        /// <summary>PostHog $set property for user properties.</summary>
        public const string Set = "$set";

        /// <summary>PostHog property to disable geo IP lookup.</summary>
        public const string GeoIpDisable = "$geoip_disable";

        /// <summary>IP address property (masked for privacy).</summary>
        public const string Ip = "$ip";

        /// <summary>Alternative IP property key.</summary>
        public const string IpAlt = "ip";

        /// <summary>Event name property.</summary>
        public const string EventName = "EventName";

        /// <summary>Log level property.</summary>
        public const string Level = "level";

        /// <summary>Message template property for log events.</summary>
        public const string MessageTemplate = "MessageTemplate";

        /// <summary>Exception details property.</summary>
        public const string Exception = "exception";

        /// <summary>Application name property.</summary>
        public const string Application = "application";

        /// <summary>Application version property.</summary>
        public const string AppVersion = "app_version";

        /// <summary>Axorith version property.</summary>
        public const string AxorithVersion = "axorith_version";

        /// <summary>Operating system version property.</summary>
        public const string OsVersion = "os_version";

        /// <summary>Build channel property (e.g., stable, beta).</summary>
        public const string BuildChannel = "build_channel";

        /// <summary>Environment property (e.g., development, production).</summary>
        public const string Environment = "environment";
    }
}