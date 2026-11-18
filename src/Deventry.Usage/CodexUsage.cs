using System.Text.Json.Nodes;
using Deventry.Core.Protocol;

namespace Deventry.Usage;

/// <summary>
/// Codex's plan limits as Deventry shows them (DESIGN.md §18, "Codex, a second agent", "The usage header"): the
/// <c>rateLimits</c> of <c>account/rateLimits/read</c> or <c>account/rateLimits/updated</c>, each window by its length. A
/// window of a day or less is the session, one of a few days or more the week; the primary and secondary windows are
/// usually five hours and a week.
/// </summary>
public static class CodexUsage
{
    /// <summary>A window this long or shorter is the session's.</summary>
    private static readonly TimeSpan SessionLongest = TimeSpan.FromDays(1);

    /// <summary>The readings in <paramref name="rateLimits"/>; null when it has neither window.</summary>
    public static UsageSnapshot? From(JsonObject rateLimits, DateTimeOffset now)
    {
        var limits = new List<LimitReading>();
        foreach (var name in new[] { "primary", "secondary" })
        {
            if (rateLimits.GetObject(name) is not { } window || window.GetDouble("usedPercent") is not { } percent)
            {
                continue;
            }
            var minutes = window.GetDouble("windowDurationMins");
            var kind = minutes is { } m && TimeSpan.FromMinutes(m) > SessionLongest ? LimitKind.WeeklyAll : name == "primary" ? LimitKind.Session : LimitKind.WeeklyAll;
            if (limits.Any(l => l.Kind == kind))
            {
                continue;
            }
            var resets = window["resetsAt"].AsWholeNumber() is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : (DateTimeOffset?)null;
            limits.Add(new LimitReading(kind, kind == LimitKind.Session ? UsageParser.SessionLabel : UsageParser.WeeklyLabel, Math.Clamp(percent, 0, 100), resets, null, true));
        }
        return limits.Count == 0 ? null : new UsageSnapshot(limits, now, UsageSource.RateLimitEvent);
    }

    /// <summary>Codex says a limit has been reached (<c>rateLimitReachedType</c>).</summary>
    public static bool IsLimitReached(JsonObject rateLimits) => rateLimits.GetString("rateLimitReachedType") is { Length: > 0 };
}
