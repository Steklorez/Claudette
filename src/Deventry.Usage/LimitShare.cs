namespace Deventry.Usage;

/// <summary>
/// A tab's estimated share of the session limit in its current window (DESIGN.md §18, "The breakdown"): between two of the
/// plan's samples, the session meter's rise is split between the tabs whose calls fell between them, by their cost. A rise
/// with no call of this machine's under it (another machine's tab that doesn't sync, claude.ai) is no tab's. Only the
/// Default account's calls count: an API key's don't use the plan.
/// </summary>
/// <param name="Points">The meter's points this tab is given.</param>
/// <param name="Rise">How far the meter rose in the window.</param>
/// <param name="Elsewhere">How much of that rose with no call here.</param>
public sealed record LimitShare(double Points, double Rise, double Elsewhere)
{
    /// <summary>How long a session window is.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(5);

    /// <summary>
    /// <paramref name="tabId"/>'s share of the latest window in <paramref name="samples"/>, from every tab's calls in it;
    /// <paramref name="planTurns"/> are the turns of the Default account's tabs. Null without a reading of the window.
    /// </summary>
    public static LimitShare? Of(string tabId, IReadOnlyList<UsageSample> samples, IReadOnlyList<CallRecord> calls, IReadOnlySet<string> planTurns)
    {
        if (samples.Where(s => s is { SessionPercent: not null, SessionResetsAt: not null }).MaxBy(s => s.Timestamp) is not { SessionResetsAt: { } resets })
        {
            return null;
        }
        var start = resets - Window;
        // The window's readings, from nothing at its start; the two sources' times differ by fractions of a second (DESIGN.md §6, "Sampling").
        var readings = samples
            .Where(s => s.SessionPercent is not null && s.SessionResetsAt is { } at && Math.Abs((at - resets).TotalSeconds) < 1 && s.Timestamp >= start)
            .OrderBy(s => s.Timestamp)
            .Select(s => (s.Timestamp, Percent: s.SessionPercent!.Value))
            .Prepend((Timestamp: start, Percent: 0.0))
            .ToList();
        var counted = calls.Where(c => planTurns.Contains(c.Turn)).ToList();
        double points = 0, rise = 0, elsewhere = 0, reached = 0;
        for (var i = 1; i < readings.Count; i++)
        {
            // Within a window usage only rises: a lower reading is a stale one.
            var gained = readings[i].Percent - reached;
            if (gained <= 0)
            {
                continue;
            }
            var from = readings[i - 1].Timestamp;
            var to = readings[i].Timestamp;
            var between = counted.Where(c => c.Timestamp > from && c.Timestamp <= to).ToList();
            var cost = between.Sum(c => c.CostUsd);
            if (cost > 0)
            {
                points += gained * between.Where(c => c.TabId == tabId).Sum(c => c.CostUsd) / cost;
            }
            else
            {
                elsewhere += gained;
            }
            rise += gained;
            reached = readings[i].Percent;
        }
        return new LimitShare(points, rise, elsewhere);
    }

    /// <summary>
    /// <paramref name="tabId"/>'s share of the current window, from the usage history. Null without a reading of it.
    /// Synchronous: keep it off the UI thread.
    /// </summary>
    public static LimitShare? Read(UsageStore store, string tabId, DateTimeOffset now)
    {
        var samples = store.GetSamples(now - Window - TimeSpan.FromMinutes(1), now);
        var plan = store.GetTurns(now - Window - TimeSpan.FromHours(1), now).Where(t => t.Account is null).Select(t => t.Turn).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return Of(tabId, samples, store.GetCalls(now - Window, now), plan);
    }
}
