using System.Diagnostics;

namespace Axorith.Shared.Utils;

public static class MonotonicTime
{
    public static long DeadlineAfter(TimeSpan duration)
    {
        var now = Stopwatch.GetTimestamp();
        var seconds = Math.Max(0, duration.TotalSeconds);
        var available = long.MaxValue - now;
        return seconds >= (double)available / Stopwatch.Frequency
            ? long.MaxValue
            : now + (long)(seconds * Stopwatch.Frequency);
    }

    public static TimeSpan RemainingUntil(long deadline)
    {
        var ticks = Math.Max(0, deadline - Stopwatch.GetTimestamp());
        return TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
    }
}
