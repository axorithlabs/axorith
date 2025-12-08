namespace Axorith.Telemetry;

/// <summary>
///     Configuration options for HTTP retry policy with exponential backoff.
/// </summary>
internal sealed class RetryPolicyOptions
{
    /// <summary>Maximum number of retry attempts before giving up.</summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>Initial delay before first retry.</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Maximum delay between retries.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Multiplier for exponential backoff (delay = InitialDelay * BackoffMultiplier^attempt).</summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    ///     Creates options from TelemetrySettings.
    /// </summary>
    public static RetryPolicyOptions FromSettings(TelemetrySettings settings)
    {
        return new RetryPolicyOptions
        {
            MaxRetryAttempts = settings.MaxRetryAttempts,
            InitialDelay = settings.InitialRetryDelay
        };
    }

    /// <summary>
    ///     Calculates the delay for a given retry attempt using exponential backoff.
    /// </summary>
    /// <param name="attempt">Zero-based retry attempt number.</param>
    /// <returns>The delay to wait before the retry.</returns>
    public TimeSpan GetDelay(int attempt)
    {
        var delay = TimeSpan.FromMilliseconds(InitialDelay.TotalMilliseconds * Math.Pow(BackoffMultiplier, attempt));
        return delay > MaxDelay ? MaxDelay : delay;
    }
}