namespace Axorith.Telemetry;

/// <summary>
///     Constants for telemetry event names and property keys.
///     Eliminates magic strings throughout the telemetry codebase.
/// </summary>
public static class TelemetryConstants
{
    /// <summary>PostHog identify event for user/device identification.</summary>
    public const string IdentifyEvent = "$identify";

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

        /// <summary>Event name property.</summary>
        public const string EventName = "EventName";

        internal const string PreferenceGeneration = "__telemetry_preference_generation";

        /// <summary>Application name property.</summary>
        public const string Application = "application";

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
