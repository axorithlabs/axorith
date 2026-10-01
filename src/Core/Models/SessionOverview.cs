namespace Axorith.Core.Models;

public sealed record SessionOverview(TimeSpan Today, TimeSpan Week, int SessionCount, double[] DailyMinutes,
    IReadOnlyList<SessionActivity> Recent, bool HasActivity)
{
    public static SessionOverview Calculate(IReadOnlyList<SessionActivity> history, DateTimeOffset? activeStartedAt,
        string activeName, DateTimeOffset now)
    {
        var activity = history.Where(session => session.EndedAt >= session.StartedAt).ToList();
        if (activeStartedAt is { } startedAt && startedAt <= now &&
            !activity.Any(session => session.StartedAt == startedAt))
            activity.Add(new SessionActivity(startedAt, now, activeName));

        var today = LocalDay(now.LocalDateTime.Date);
        var week = LocalDay(now.LocalDateTime.Date.AddDays(-6));
        var daily = Enumerable.Range(0, 7).Select(day =>
            Overlap(activity, LocalDay(now.LocalDateTime.Date.AddDays(day - 6)),
                LocalDay(now.LocalDateTime.Date.AddDays(day - 5)), now).TotalMinutes).ToArray();
        return new SessionOverview(Overlap(activity, today, now, now), Overlap(activity, week, now, now),
            activity.Count(session => session.StartedAt >= week && session.StartedAt <= now), daily,
            history.OrderByDescending(session => session.EndedAt).Take(5).ToArray(), activity.Count > 0);
    }

    private static DateTimeOffset LocalDay(DateTime date) => new(date, TimeZoneInfo.Local.GetUtcOffset(date));

    private static TimeSpan Overlap(IEnumerable<SessionActivity> activity, DateTimeOffset from,
        DateTimeOffset until, DateTimeOffset now)
    {
        var ticks = activity.Sum(session =>
        {
            var start = session.StartedAt > from ? session.StartedAt : from;
            var end = session.EndedAt < until ? session.EndedAt : until;
            if (end > now) end = now;
            return Math.Max(0, (end - start).Ticks);
        });
        return TimeSpan.FromTicks(ticks);
    }
}
