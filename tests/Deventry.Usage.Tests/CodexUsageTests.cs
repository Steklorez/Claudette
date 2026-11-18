using System.Text.Json.Nodes;

namespace Deventry.Usage.Tests;

/// <summary>Codex's plan limits as the header's readings (DESIGN.md §18, "Codex, a second agent", "The usage header").</summary>
public sealed class CodexUsageTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_five_hour_window_is_the_session_and_the_week_is_weekly()
    {
        // As account/rateLimits/updated carried them in the spike.
        var limits = JsonNode.Parse("""
            {"limitId":"codex","primary":{"usedPercent":42,"windowDurationMins":300,"resetsAt":1794956704},
             "secondary":{"usedPercent":17,"windowDurationMins":10080,"resetsAt":1795211104},"rateLimitReachedType":null}
            """)!.AsObject();

        var snapshot = CodexUsage.From(limits, Now)!;

        Assert.Equal((42, DateTimeOffset.FromUnixTimeSeconds(1794956704)), (snapshot.Session!.Percent, snapshot.Session.ResetsAt));
        Assert.Equal(17, snapshot.WeeklyAll!.Percent);
        Assert.Equal(("Session", "Weekly"), (snapshot.Session.Label, snapshot.WeeklyAll.Label));
        Assert.False(CodexUsage.IsLimitReached(limits));
    }

    [Fact]
    public void A_primary_window_of_a_week_is_weekly()
    {
        var limits = JsonNode.Parse("""{"primary":{"usedPercent":60,"windowDurationMins":10080}}""")!.AsObject();

        var snapshot = CodexUsage.From(limits, Now)!;

        Assert.Null(snapshot.Session);
        Assert.Equal(60, snapshot.WeeklyAll!.Percent);
    }

    [Fact]
    public void Without_a_window_there_is_nothing_to_show()
    {
        // An API key, or the mock: Codex sends the bucket with no windows.
        var limits = JsonNode.Parse("""{"limitId":"codex","primary":null,"secondary":null}""")!.AsObject();

        Assert.Null(CodexUsage.From(limits, Now));
    }

    [Fact]
    public void A_reached_limit_says_so() =>
        Assert.True(CodexUsage.IsLimitReached(JsonNode.Parse("""{"rateLimitReachedType":"primary"}""")!.AsObject()));
}
