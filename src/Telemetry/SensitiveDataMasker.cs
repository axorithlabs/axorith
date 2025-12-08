using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Axorith.Telemetry;

/// <summary>
///     Provides methods for masking sensitive data in telemetry payloads.
///     Uses FrozenSet for optimal lookup performance.
/// </summary>
internal static partial class SensitiveDataMasker
{
    /// <summary>Mask value used to replace sensitive data.</summary>
    public const string MaskValue = "***";

    /// <summary>
    ///     Keys that contain sensitive information and should be masked.
    /// </summary>
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

    /// <summary>
    ///     Keys related to geographic/location data that should be excluded entirely.
    /// </summary>
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

    /// <summary>
    ///     Keys that are exempt from IP masking (version strings, etc.).
    /// </summary>
    private static readonly FrozenSet<string> IpMaskExemptKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "app_version",
        "axorith_version",
        "application",
        "os_version",
        "build_channel",
        "environment"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // IPv4: d.d.d.d where d is 1-3 digits
    [GeneratedRegex(@"^(?:\d{1,3}\.){3}\d{1,3}$", RegexOptions.Compiled)]
    private static partial Regex IPv4Regex();

    // IPv6 full format: 8 groups of 4 hex digits separated by colons
    [GeneratedRegex(@"^([0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}$", RegexOptions.Compiled)]
    private static partial Regex IPv6FullRegex();

    // IPv6 compressed format: ::1, fe80::, 2001:db8::1, etc.
    [GeneratedRegex(@"^([0-9a-fA-F]{0,4}:){2,7}[0-9a-fA-F]{0,4}$", RegexOptions.Compiled)]
    private static partial Regex IPv6CompressedRegex();

    // IPv4-mapped IPv6: ::ffff:d.d.d.d
    [GeneratedRegex(@"^::ffff:(?:\d{1,3}\.){3}\d{1,3}$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex IPv4MappedIPv6Regex();

    // Loopback IPv6: ::1
    [GeneratedRegex(@"^::1$", RegexOptions.Compiled)]
    private static partial Regex IPv6LoopbackRegex();

    /// <summary>
    ///     Checks if the given key is a sensitive key that should be masked.
    /// </summary>
    public static bool IsSensitiveKey(string? key)
    {
        return !string.IsNullOrEmpty(key) && SensitiveKeys.Contains(key);
    }

    /// <summary>
    ///     Checks if the given key is a geo-related key that should be excluded.
    /// </summary>
    public static bool IsGeoKey(string? key)
    {
        return !string.IsNullOrEmpty(key) && GeoKeys.Contains(key);
    }

    /// <summary>
    ///     Checks if the given key is exempt from IP masking.
    /// </summary>
    public static bool IsIpMaskExempt(string? key)
    {
        return !string.IsNullOrEmpty(key) && IpMaskExemptKeys.Contains(key);
    }

    /// <summary>
    ///     Masks the value if it appears to be an IP address (IPv4 or IPv6).
    /// </summary>
    /// <param name="value">The string value to check.</param>
    /// <returns>
    ///     The masked value "***" if it's an IP address, otherwise the original value trimmed. Returns empty string for
    ///     null/whitespace input.
    /// </returns>
    public static string MaskIfIpAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value ?? string.Empty;
        }

        var trimmed = value.Trim();

        return IsIpAddress(trimmed) ? MaskValue : trimmed;
    }

    /// <summary>
    ///     Checks if the given string is a valid IP address (IPv4 or IPv6).
    /// </summary>
    private static bool IsIpAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        return IPv4Regex().IsMatch(trimmed) ||
               IPv6FullRegex().IsMatch(trimmed) ||
               IPv6CompressedRegex().IsMatch(trimmed) ||
               IPv4MappedIPv6Regex().IsMatch(trimmed) ||
               IPv6LoopbackRegex().IsMatch(trimmed);
    }
}