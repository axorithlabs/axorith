namespace Axorith.Telemetry;

public static class TelemetryConstants
{
    public const string IdentifyEvent = "$identify";

    public const string DefaultEvent = "event";

    public static class Properties
    {
        public const string DistinctId = "distinct_id";

        public const string Set = "$set";

        public const string EventName = "EventName";

        internal const string PreferenceGeneration = "__telemetry_preference_generation";
        internal const string GeoIpDisable = "$geoip_disable";

        public const string Application = "application";

        public const string AxorithVersion = "axorith_version";

        public const string OsVersion = "os_version";

        public const string BuildChannel = "build_channel";

        public const string Environment = "environment";
    }
}
