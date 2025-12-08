using Serilog.Events;

namespace Axorith.Telemetry;

/// <summary>
///     Telemetry settings with hard defaults. CI patches key/host directly here.
///     Uses record type for immutable updates via 'with' expressions.
/// </summary>
public sealed record TelemetrySettings
{
    public bool Enabled { get; init; } =
        Environment.GetEnvironmentVariable("AXORITH_TELEMETRY", EnvironmentVariableTarget.User) == "1" ||
        !string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable("AXORITH_TELEMETRY_API_KEY", EnvironmentVariableTarget.User));

    public string DistinctId { get; init; } = string.Empty;
    public string PostHogApiKey { get; init; } = "##POSTHOG_API_KEY##";
    public string PostHogHost { get; init; } = "https://us.i.posthog.com";
    public int BatchSize { get; init; } = 20;
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int QueueLimit { get; init; } = 256;
    public string AppVersion { get; init; } = string.Empty;
    public string OsVersion { get; init; } = string.Empty;
    public string ApplicationName { get; init; } = string.Empty;
    public string? BuildChannel { get; init; }
    public string? EnvironmentOverride { get; init; }
    public string? LogLevel { get; init; }
    public int MaxRetryAttempts { get; init; } = 3;
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Returns true if telemetry is enabled and properly configured.
    ///     Checks for placeholder API key pattern (##...##) to detect unconfigured state.
    /// </summary>
    public bool IsActive =>
        Enabled &&
        !string.IsNullOrWhiteSpace(PostHogApiKey) &&
        !PostHogApiKey.StartsWith("##", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(PostHogHost);

    /// <summary>
    ///     Returns a new instance with environment variable overrides applied.
    /// </summary>
    public TelemetrySettings WithEnvironmentOverrides()
    {
        var envApiKey = Environment.GetEnvironmentVariable("AXORITH_TELEMETRY_API_KEY", EnvironmentVariableTarget.User);

        if (string.IsNullOrWhiteSpace(envApiKey))
        {
            return this;
        }

        return this with { PostHogApiKey = envApiKey };
    }

    public static LogEventLevel ResolveLogLevel(string? configuredLevel = null)
    {
        var value = string.IsNullOrWhiteSpace(configuredLevel) ? "Warning" : configuredLevel;

        return Enum.TryParse<LogEventLevel>(value, true, out var parsed)
            ? parsed
            : LogEventLevel.Warning;
    }
}

public static class TelemetryGuard
{
    public static string SafeString(string? value, int maxLength = 1_024)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    public static string SafeStackTrace(Exception? ex, int maxLength = 2_048)
    {
        if (ex == null)
        {
            return string.Empty;
        }

        var stack = ex.ToString();
        return SafeString(stack, maxLength);
    }
}