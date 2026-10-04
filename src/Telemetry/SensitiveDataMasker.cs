using System.Collections.Frozen;

namespace Axorith.Telemetry;

internal static class SensitiveDataMasker
{
    public const string MaskValue = "***";

    private static readonly FrozenSet<string> SensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Authentication & secrets
        "token",
        "password",
        "secret",
        "key",
        "api_key",
        "apikey",
        "private_key",
        "auth",
        "authorization",
        "bearer",
        "refresh_token",
        "access_token",
        "credential",
        "cookie",
        "session",

        // Network identifiers
        "ip",
        "client_ip",
        "remote_ip",
        "x_forwarded_for",
        "xff",
        "$ip",

        // PII
        "email",
        "phone",
        "address",
        "ssn",
        "credit_card",
        "card_number",

        // Location
        "location",
        "lat",
        "lon",
        "gps"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> GeoKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ip",
        "$ip",
        "client_ip",
        "remote_ip",
        "x_forwarded_for",
        "xff",
        "city",
        "continent",
        "continent_code",
        "continent_name",
        "country",
        "country_code",
        "country_name",
        "latitude",
        "longitude",
        "lat",
        "lon",
        "lng",
        "postal_code",
        "zip",
        "timezone",
        "region",
        "state",
        "province",
        "subdivision_1_code",
        "subdivision_1_name",
        "subdivision_2_code",
        "subdivision_2_name",
        "geoip",
        "location"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsSensitiveKey(string? key) => !string.IsNullOrEmpty(key) && SensitiveKeys.Contains(key);

    public static bool IsGeoKey(string? key) => !string.IsNullOrEmpty(key) && GeoKeys.Contains(key);
}
