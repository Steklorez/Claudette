using Deventry.Core.Sessions;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Usage.Tests;

/// <summary>What a tab's tokens went on (DESIGN.md §18, "The breakdown").</summary>
public sealed class TokenBreakdownTests : IDisposable
{
    private const string Tab = "tab-1";
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"deventry-breakdown-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // A tab of four turns:
    // 1. recorded before calls were, on Opus and Claude Code's Haiku;
    // 2. two calls with a 4k Read between them;
    // 3. sent 70 minutes later, after the cache expired, with an Explore subagent of three calls and Claude Code's own call on
    //    Haiku while it fetched a page;
    // 4. compacted first, then two calls.
    private static readonly TurnRecord[] Turns =
    [
        new(T0.AddMinutes(1), Tab, "s1", "opus", 10, 500, 20_000, 0, 0.30),
        new(T0.AddMinutes(1), Tab, "s1", "haiku", 1_200, 20, 0, 0, 0.002),
        new TurnRecord(T0.AddMinutes(10).AddSeconds(10), Tab, "s1", "opus", 5, 400, 3_100, 40_600, 0.12) { Turn = "t2", IdleBefore = TimeSpan.FromMinutes(9) },
        new TurnRecord(T0.AddMinutes(80).AddSeconds(30), Tab, "s1", "opus", 20, 1_000, 43_500, 57_000, 1.45) { Turn = "t3", IdleBefore = TimeSpan.FromMinutes(70) },
        new TurnRecord(T0.AddMinutes(80).AddSeconds(30), Tab, "s1", "haiku", 40_000, 100, 0, 0, 0.15) { Turn = "t3", IdleBefore = TimeSpan.FromMinutes(70) },
        new TurnRecord(T0.AddMinutes(90).AddSeconds(30), Tab, "s1", "opus", 10, 5_250, 8_200, 68_100, 0.46) { Turn = "t4", IdleBefore = TimeSpan.FromMinutes(9) },
    ];

    private static readonly CallRecord[] Calls =
    [
        Call("t2", 1, T0.AddMinutes(10), input: 3, output: 100, read: 20_000, write1h: 600, cost: 0.05),
        Call("t2", 2, T0.AddMinutes(10).AddSeconds(5), input: 2, output: 300, read: 20_600, write1h: 2_500, cost: 0.07),
        Call("t3", 1, T0.AddMinutes(80), input: 3, output: 100, read: 0, write1h: 25_000, cost: 0.60, kind: CallKind.CacheExpired),
        Call("t3", 2, T0.AddMinutes(80).AddSeconds(5), input: 5, output: 200, read: 0, write5m: 15_000, cost: 0.30, agent: "toolu_a1"),
        Call("t3", 3, T0.AddMinutes(80).AddSeconds(10), input: 5, output: 200, read: 15_000, write5m: 2_000, cost: 0.20, agent: "toolu_a1"),
        Call("t3", 4, T0.AddMinutes(80).AddSeconds(15), input: 5, output: 300, read: 17_000, write5m: 1_000, cost: 0.25, agent: "toolu_a1"),
        Call("t3", 5, T0.AddMinutes(80).AddSeconds(20), input: 2, output: 200, read: 25_000, write1h: 500, cost: 0.10),
        Call("t3", 6, T0.AddMinutes(80).AddSeconds(30), input: 40_000, output: 100, read: 0, cost: 0.15, kind: CallKind.ClaudeCode, model: "haiku"),
        Call("t4", 1, T0.AddMinutes(90).AddSeconds(5), input: 3, output: 100, read: 0, write1h: 8_000, cost: 0.18),
        Call("t4", 2, T0.AddMinutes(90).AddSeconds(10), input: 2, output: 150, read: 8_100, write1h: 200, cost: 0.03),
        Call("t4", 3, T0.AddMinutes(90).AddSeconds(30), input: 5, output: 5_000, read: 60_000, cost: 0.25, kind: CallKind.Compaction),
    ];

    private static readonly StepRecord[] Steps =
    [
        new("t2", 1, null, "Read", 4_000, false, T0.AddMinutes(10).AddSeconds(2)) { ToolUseId = "toolu_r1" },
        new("t3", 2, "toolu_a1", "Grep", 50, false, T0.AddMinutes(80).AddSeconds(7)) { ToolUseId = "toolu_g1" },
        new("t3", 1, null, "Agent", 300, false, T0.AddMinutes(80).AddSeconds(18)) { ToolUseId = "toolu_a1" },
        new("t3", 5, null, "WebFetch", 500, false, T0.AddMinutes(80).AddSeconds(22)) { ToolUseId = "toolu_w1" },
    ];

    private static readonly UsageEvent[] Events = [new(Tab, "s1", "t4", T0.AddMinutes(90), UsageEventKind.Compacted, 60_000, "auto")];

    private static TokenBreakdown Breakdown() => TokenBreakdown.From(Turns, Calls, Steps, Events);

    [Fact]
    public void A_turn_recorded_with_its_number_keeps_it()
    {
        TurnRecord[] numbered = [.. Turns.Select(t => t.Turn switch { "t3" => t with { Index = 12 }, "t4" => t with { Index = 13 }, _ => t })];

        var breakdown = TokenBreakdown.From(numbered, Calls, Steps, Events);

        // As the turns' cards say them: a tab's turns with no usage aren't recorded, and turns from before numbers by their place.
        Assert.Equal([1, 2, 12, 13], breakdown.Turns.Select(t => t.Number));
        Assert.Equal(12, breakdown.Findings.OfType<CacheExpiredFinding>().Single().Turn);
    }

    [Fact]
    public void Turns_are_numbered_in_their_tab_and_add_up_to_their_records()
    {
        var breakdown = Breakdown();

        Assert.Equal([1, 2, 3, 4], breakdown.Turns.Select(t => t.Number));
        Assert.False(breakdown.Turns[0].HasCalls);
        Assert.Equal(["opus", "haiku"], breakdown.Turns[0].Models);
        Assert.Equal(Turns.Sum(t => t.Total), breakdown.Tokens.Total);
        Assert.Equal(Turns.Sum(t => t.CostUsd), breakdown.CostUsd, 9);
        Assert.Equal(11, breakdown.CallCount);
    }

    [Fact]
    public void A_turn_says_its_calls_its_marks_and_the_context_after_it()
    {
        var turns = Breakdown().Turns;

        var cold = turns[2];
        Assert.True(cold.CacheExpired);
        Assert.Equal((2, 3, 3), (cold.MainCalls, cold.SubagentCalls, cold.ToolCalls));
        Assert.Equal(["Explore"], cold.AgentTypes);
        Assert.Equal(TimeSpan.FromMinutes(70), cold.IdleBefore);
        // The main agent's last call: 25,000 read, 500 written, 2 in and 200 out.
        Assert.Equal(25_702, cold.ContextAfter);
        Assert.Equal(57_000.0 / (57_000 + 43_500 + 40_020), cold.Tokens.CacheShare!.Value, 9);

        Assert.True(turns[3].Compacted);
        Assert.False(turns[1].Compacted);
        Assert.Null(turns[0].ContextAfter);
    }

    [Fact]
    public void A_calls_cost_is_shared_between_its_kinds_by_their_weighted_tokens()
    {
        // 100 in, 1,000 read (100), 100 written for an hour (200) and 100 out (500): 900 in all.
        var costs = KindCosts.Of(Call("t", 1, T0, input: 100, output: 100, read: 1_000, write1h: 100, cost: 0.9));

        Assert.Equal((0.1, 0.1, 0.2, 0.5), (Math.Round(costs.Input, 9), Math.Round(costs.CacheRead, 9), Math.Round(costs.CacheWrite, 9), Math.Round(costs.Output, 9)));
        Assert.Equal(Calls.Sum(c => c.CostUsd), Calls.Sum(c => KindCosts.Of(c).Total), 9);
    }

    [Fact]
    public void Prices_come_from_claude_codes_costs()
    {
        var prices = Breakdown().Prices;

        var opus = Calls.Where(c => c.Model == "opus").ToList();
        Assert.Equal(opus.Sum(c => c.CostUsd) / opus.Sum(TokenPrices.Weigh), prices.Input("opus")!.Value, 12);
        Assert.Equal(prices.Input("opus")!.Value * 0.1, prices.CacheRead("opus")!.Value, 12);
        Assert.Equal(prices.Input("opus")!.Value * 2, prices.CacheWrite("opus", hour: true)!.Value, 12);
        Assert.Null(prices.Input("sonnet"));
    }

    [Fact]
    public void Models_agents_and_tools_each_have_their_share()
    {
        var breakdown = Breakdown();

        Assert.Equal([("opus", 10), ("haiku", 1)], breakdown.ByModel.Select(m => (m.Model, m.Calls)));
        // The turn without calls counts each model's record.
        Assert.Equal(1_200 + 20 + 40_100, breakdown.ByModel[1].Tokens.Total);

        Assert.Equal([AgentKind.Main, AgentKind.Subagent, AgentKind.ClaudeCode], breakdown.ByAgent.Select(a => a.Kind));
        var explore = breakdown.ByAgent[1];
        Assert.Equal(("toolu_a1", "Explore", 3, 3, 300L), (explore.Agent, explore.Type, explore.Turn, explore.Calls, explore.ReportTokens));
        Assert.Equal(0.75, explore.CostUsd, 9);
        Assert.Equal(7, breakdown.ByAgent[0].Calls);
        Assert.Equal(breakdown.CostUsd, breakdown.ByAgent.Sum(a => a.CostUsd), 9);

        // The Read was read again by the main agent's three calls before the compaction; the subagent's Grep by its two later
        // calls; the WebFetch by none, being the main agent's last before the compaction.
        Assert.Equal([("Read", 12_000L), ("Agent", 300L), ("Grep", 100L), ("WebFetch", 0L)], breakdown.ByTool.Select(t => (t.Tool, t.ReadAgain)));
        Assert.Equal(4_000, breakdown.ByTool[0].Added);
        Assert.Equal(12_000 * breakdown.Prices.CacheRead("opus")!.Value, breakdown.ByTool[0].ReadAgainCostUsd, 12);
    }

    [Fact]
    public void Findings_say_what_cost_more_than_it_needed_to_the_costliest_first()
    {
        var findings = Breakdown().Findings;
        static string Name(string model) => model == "opus" ? "Opus" : "Haiku";

        Assert.Equal(
            [TokenFindingKind.CostlySubagent, TokenFindingKind.CacheExpired, TokenFindingKind.Compaction, TokenFindingKind.ClaudeCode, TokenFindingKind.HeavyResult],
            findings.Select(f => f.Kind));
        Assert.Equal("The Explore subagent in turn 3 made 3 calls on Opus: 50.7k tokens, $0.75 (30%), for a report of 300 tokens.", findings[0].Describe(Name));
        Assert.Equal("Turn 3 was sent 1h 10m after turn 2, after the 1-hour prompt cache had expired. Its first call wrote the whole conversation to the cache again: "
            + "25k tokens, $0.60, 24% of this tab's cost.", findings[1].Describe(Name));
        var compaction = Assert.IsType<CompactionFinding>(findings[2]);
        Assert.Equal((60_000L, 8_103L), (compaction.Before, compaction.After));
        Assert.Equal((0.05 + 0.07 + 0.60 + 0.10) / 4, compaction.CallCostBefore!.Value, 9);
        Assert.Equal((0.18 + 0.03) / 2, compaction.CallCostAfter!.Value, 9);
        Assert.Equal("In turn 3, Claude Code made calls of its own that it doesn't show, such as reading the pages WebFetch fetched: 40.1k tokens on Haiku, $0.15.",
            findings[3].Describe(Name));
        Assert.Equal("The Read result in turn 2 (4k tokens) stayed in the conversation for 3 more calls, until it was compacted: 12k tokens read again.", findings[4].Describe(Name));
    }

    [Fact]
    public void A_subagent_of_the_same_type_that_ran_cheaper_is_named()
    {
        var calls = Calls.Concat(
        [
            Call("t2", 3, T0.AddMinutes(10).AddSeconds(6), input: 5, output: 50, read: 0, write5m: 3_000, cost: 0.01, agent: "toolu_cheap", model: "haiku"),
        ]).Select(c => c.Agent == "toolu_cheap" ? c with { AgentType = "Explore" } : c).ToList();
        var turns = Turns.Append(new TurnRecord(T0.AddMinutes(10).AddSeconds(10), Tab, "s1", "haiku", 5, 50, 3_000, 0, 0.01) { Turn = "t2" }).ToList();

        var finding = Assert.IsType<CostlySubagentFinding>(TokenBreakdown.From(turns, calls, Steps, Events).Findings[0]);

        Assert.Equal(new CheaperRun(2, "haiku", 0.01), finding.Cheaper);
        Assert.EndsWith(" Turn 2's Explore ran on Haiku for $0.01.", finding.Describe(m => m == "opus" ? "Opus" : "Haiku"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_period_keeps_its_turns_numbers_and_a_tab_reads_from_the_history()
    {
        Directory.CreateDirectory(_root);
        using var store = new UsageStore(Path.Combine(_root, "usage.db"), new FakeTimeProvider(T0.AddHours(2)));
        store.AddTurns(Turns.Where(t => t.Turn is null));
        foreach (var turn in Turns.Where(t => t.Turn is not null).GroupBy(t => t.Turn))
        {
            store.AddTurns(turn, new TurnDetail([.. Calls.Where(c => c.Turn == turn.Key)], [.. Steps.Where(s => s.Turn == turn.Key)], [.. Events.Where(e => e.Turn == turn.Key)]));
        }

        var read = TokenBreakdown.Read(store, Tab, T0.AddMinutes(5), T0.AddHours(2));

        Assert.Equal([2, 3, 4], read.Turns.Select(t => t.Number));
        Assert.Equal(Turns.Where(t => t.Turn is not null).Sum(t => t.Total), read.Tokens.Total);
        Assert.Equal(TokenFindingKind.CostlySubagent, read.Findings[0].Kind);
        Assert.Equal(12_000, read.ByTool[0].ReadAgain);
    }

    // ---- The share of the session limit -------------------------------------------------------------------------------

    [Fact]
    public void The_meters_rise_is_split_between_the_tabs_working_then_by_their_cost()
    {
        var resets = T0.AddHours(5);
        UsageSample[] samples =
        [
            Sample(T0.AddMinutes(30), 20, resets),
            Sample(T0.AddMinutes(60), 30, resets),
            // No rise, then a stale reading, then a rise with no call here.
            Sample(T0.AddMinutes(70), 30, resets),
            Sample(T0.AddMinutes(80), 25, resets),
            Sample(T0.AddMinutes(90), 40, resets),
        ];
        CallRecord[] calls =
        [
            // Up to the first reading, both tabs alike: ten points each.
            Call("a1", 1, T0.AddMinutes(10), 0, 0, 0, cost: 1) with { TabId = "tab-a" },
            Call("b1", 1, T0.AddMinutes(20), 0, 0, 0, cost: 1) with { TabId = "tab-b" },
            // Then only tab-b, and an API key's tab, which doesn't use the plan.
            Call("b2", 1, T0.AddMinutes(40), 0, 0, 0, cost: 1) with { TabId = "tab-b" },
            Call("k1", 1, T0.AddMinutes(50), 0, 0, 0, cost: 5) with { TabId = "tab-a" },
        ];
        var plan = new HashSet<string> { "a1", "b1", "b2" };

        Assert.Equal(new LimitShare(10, 40, 10), LimitShare.Of("tab-a", samples, calls, plan));
        Assert.Equal(new LimitShare(20, 40, 10), LimitShare.Of("tab-b", samples, calls, plan));
        Assert.Null(LimitShare.Of("tab-a", [], calls, plan));
    }

    [Fact]
    public void Only_the_latest_windows_readings_count()
    {
        var earlier = T0.AddHours(1);
        var resets = T0.AddHours(6);
        UsageSample[] samples = [Sample(T0.AddMinutes(10), 90, earlier), Sample(T0.AddMinutes(90), 15, resets.AddMilliseconds(400))];
        CallRecord[] calls = [Call("a1", 1, T0.AddMinutes(80), 0, 0, 0, cost: 1) with { TabId = "tab-a" }];

        Assert.Equal(new LimitShare(15, 15, 0), LimitShare.Of("tab-a", samples, calls, new HashSet<string> { "a1" }));
    }

    private static UsageSample Sample(DateTimeOffset at, double session, DateTimeOffset resets) => new(at, session, resets, null, null, []);

    private static CallRecord Call(string turn, int seq, DateTimeOffset at, long input, long output, long read, long write5m = 0, long write1h = 0, double cost = 0,
        CallKind kind = CallKind.Call, string? agent = null, string model = "opus") =>
        new(turn, Tab, "s1", at, seq, kind is CallKind.Compaction or CallKind.ClaudeCode ? null : $"msg_{turn}_{seq}", agent, agent is null ? null : "Explore", model,
            input, output, read, write5m, write1h, 0, 0, kind, cost);
}
