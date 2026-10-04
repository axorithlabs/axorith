namespace Axorith.Telemetry;

internal sealed class RetryPolicyOptions
{
    public int MaxRetryAttempts { get; init; } = 3;

    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    public double BackoffMultiplier { get; init; } = 2.0;

    public static RetryPolicyOptions FromSettings(TelemetrySettings settings)
    {
        return new RetryPolicyOptions
        {
            MaxRetryAttempts = settings.MaxRetryAttempts,
            InitialDelay = settings.InitialRetryDelay
        };
    }

    public TimeSpan GetDelay(int attempt)
    {
        var delay = TimeSpan.FromMilliseconds(InitialDelay.TotalMilliseconds * Math.Pow(BackoffMultiplier, attempt));
        return delay > MaxDelay ? MaxDelay : delay;
    }
}
