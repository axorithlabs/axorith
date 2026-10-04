namespace Axorith.Telemetry;

public sealed record TelemetrySettings
{
    public bool Enabled { get; init; } = true;

    public string DistinctId { get; init; } = string.Empty;
    public string PostHogApiKey { get; init; } = "phc_5JRZwyXJmWlMk1dln5MK3PipGNnAZNmRObSWLqjpMOT";
    public string PostHogHost { get; init; } = "https://us.i.posthog.com";
    public int BatchSize { get; init; } = 20;
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int QueueLimit { get; init; } = 256;
    public string AppVersion { get; init; } = string.Empty;
    public string OsVersion { get; init; } = string.Empty;
    public string ApplicationName { get; init; } = string.Empty;
    public string? BuildChannel { get; init; }
    public string? EnvironmentOverride { get; init; }
    public int MaxRetryAttempts { get; init; } = 3;
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PostHogApiKey) &&
        !PostHogApiKey.StartsWith("##", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(PostHogHost);

    public bool IsActive => Enabled && IsConfigured;

    public TelemetrySettings WithEnvironmentOverrides()
    {
        var envApiKey = Environment.GetEnvironmentVariable("AXORITH_TELEMETRY_API_KEY", EnvironmentVariableTarget.User);

        if (string.IsNullOrWhiteSpace(envApiKey))
        {
            return this;
        }

        return this with { PostHogApiKey = envApiKey };
    }

}

public static class TelemetryGuard
{
    private static readonly string UserProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string UserName = Environment.UserName;

    public static string SafeString(string? value, int maxLength = 1_024)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }


    public static string SafePath(string? path, int maxLength = 1_024)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var masked = path;

        // Replace user profile path (e.g., C:\Users\username -> C:\Users\[USER])
        if (!string.IsNullOrEmpty(UserProfilePath))
        {
            masked = masked.Replace(UserProfilePath,
                Path.Combine(Path.GetDirectoryName(UserProfilePath) ?? "C:\\Users", "[USER]"),
                StringComparison.OrdinalIgnoreCase);
        }

        // Replace username in paths (fallback for cases where profile path doesn't match)
        if (!string.IsNullOrEmpty(UserName))
        {
            masked = masked.Replace(UserName, "[USER]", StringComparison.OrdinalIgnoreCase);
        }

        return SafeString(masked, maxLength);
    }
}

