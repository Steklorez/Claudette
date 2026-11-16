using System.Text.Json.Nodes;
using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core;
using Deventry.Usage;

namespace Deventry.App.Tests;

/// <summary>
/// A tab's tokens beside its conversation (DESIGN.md §18, "The Tokens tool window", "A turn's card, the popup and the info
/// card"): the turn running, the next message, the tab's latest turns, and the turn's card.
/// </summary>
public class TabTokensTests
{
    // The harness's clock starts on Monday 28 September 2026 at 12:00 UTC, and its time zone is UTC.

    private const string Opus = "claude-opus-5-5";

    [Fact]
    public async Task The_turn_running_shows_each_call_as_it_arrives_and_the_result_after_it()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        Assert.False(tab.Tokens.HasCalls);
        // The rows are made while the window shows them.
        tab.Tokens.SetWanted("window", true);
        tab.ComposerText = "Read it";
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.Emit(new JsonObject { ["type"] = "system", ["subtype"] = "init", ["session_id"] = "s1", ["model"] = Opus, ["permissionMode"] = "default" });

        h.Transport.Emit(Assistant("msg_1", input: 3, output: 40, cacheRead: 20_000, write1h: 300, new JsonObject
        {
            ["type"] = "tool_use", ["id"] = "toolu_1", ["name"] = "Read", ["input"] = new JsonObject { ["file_path"] = "a.txt" },
        }));
        await TabTestHarness.Eventually(() => tab.Tokens.RunningCalls.Count == 1, "the first call");
        Assert.True(tab.Tokens.IsRunning);
        Assert.True(tab.Tokens.HasCalls);
        Assert.Equal("Turn 1", tab.Tokens.RunningTitle);
        // No result has priced the model yet: tokens alone.
        Assert.Equal($"{Formats.Tokens(20_343)} tokens", tab.Tokens.RunningSummary);

        h.Transport.Emit(new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = "toolu_1", ["content"] = new string('x', 800) }),
            },
        });
        await TabTestHarness.Eventually(() => tab.Tokens.IsChoosing, "Claude choosing its next step");
        Assert.Equal(["c1", ""], tab.Tokens.RunningCalls.Select(c => c.Label));
        Assert.Equal("↳ Read · +200", tab.Tokens.RunningCalls[1].What);

        h.Transport.Emit(Assistant("msg_2", input: 2, output: 120, cacheRead: 20_300, write1h: 240, new JsonObject { ["type"] = "text", ["text"] = "Done." }));
        await TabTestHarness.Eventually(() => tab.Tokens.RunningCalls.Count == 3, "the second call");
        Assert.False(tab.Tokens.IsChoosing);
        // The context is the last call's.
        Assert.Equal($"{Formats.Tokens(20_662)} of {Formats.Tokens(200_000)}", tab.Tokens.ContextText);

        h.Transport.Emit(Result());
        await TabTestHarness.Eventually(() => tab.IsSettled && !tab.Tokens.IsRunning, "the end of the turn");
        Assert.Empty(tab.Tokens.RunningCalls);
        Assert.True(tab.Tokens.HasCalls);
    }

    [Fact]
    public async Task The_rows_are_made_only_while_something_shows_them()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        tab.ComposerText = "Read it";
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.Emit(new JsonObject { ["type"] = "system", ["subtype"] = "init", ["session_id"] = "s1", ["model"] = Opus, ["permissionMode"] = "default" });
        h.Transport.Emit(Assistant("msg_1", input: 3, output: 40, cacheRead: 20_000, write1h: 300, new JsonObject { ["type"] = "text", ["text"] = "Working." }));
        await TabTestHarness.Eventually(() => tab.Tokens.IsRunning, "the turn running");
        Assert.Empty(tab.Tokens.RunningCalls);

        tab.Tokens.SetWanted("popup", true);

        Assert.Single(tab.Tokens.RunningCalls);
        Assert.Equal(Formats.Tokens(20_343), tab.Tokens.Tiles[0].Value);
    }

    [Fact]
    public async Task A_turns_card_has_its_number_its_cost_and_its_kinds_and_opens_the_breakdown_on_it()
    {
        await using var h = new TabTestHarness();
        var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        var tab = await h.OpenTabAsync();
        (TabViewModel? Tab, string? TurnId, int? Number)? asked = null;
        h.Shell.TokenBreakdownRequested += (t, id, number) => asked = ((t as OpenTabBreakdown)?.Tab, id, number);

        await SendTurnAsync(h, tab, "Read it");

        var card = tab.Items.OfType<TurnSummaryItem>().Last();
        var recorded = Assert.Single(tracker.Store.GetTurns(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, tab.Id), t => t.Model == Opus);
        Assert.Equal(("Turn 1", recorded.Turn), (card.Label, card.TurnId));
        Assert.Equal(1, recorded.Index);
        Assert.True(card.HasBreakdown);
        Assert.Contains("$0.04", card.StatsText, StringComparison.Ordinal);
        Assert.Equal(42_225, card.Kinds!.Total);
        Assert.False(card.HasCacheChip);

        tab.ShowTokenBreakdownAtCommand.Execute(card.TurnId);

        Assert.Equal((tab, card.TurnId, (int?)null), asked);
    }

    [Fact]
    public async Task The_breakdown_opens_on_the_turn_asked_for_once_it_has_been_read()
    {
        await using var h = new TabTestHarness();
        var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        var tab = await h.OpenTabAsync();
        await SendTurnAsync(h, tab, "Read it");
        h.Time.Advance(TimeSpan.FromMinutes(2));
        await SendTurnAsync(h, tab, "And again", calls: 6);
        var first = tab.Items.OfType<TurnSummaryItem>().First();
        using var breakdown = new TokenBreakdownViewModel(h.Services, tab);

        breakdown.Show(first.TurnId, null);
        breakdown.Refresh();

        await TabTestHarness.Eventually(() => breakdown.SelectedTurn == 1, "the first turn shown");
        Assert.True(breakdown.Turns[0].IsExpanded);

        breakdown.Show(null, 2);

        Assert.Equal(2, breakdown.SelectedTurn);
    }

    [Fact]
    public async Task The_tabs_latest_turns_and_info_are_read_while_something_shows_them()
    {
        await using var h = new TabTestHarness();
        var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        var tab = await h.OpenTabAsync();
        await SendTurnAsync(h, tab, "Read the grapple code\nand say what's wrong");
        (TabViewModel? Tab, string? TurnId, int? Number)? asked = null;
        h.Shell.TokenBreakdownRequested += (t, id, number) => asked = ((t as OpenTabBreakdown)?.Tab, id, number);

        tab.Tokens.SetWanted("window", true);

        await TabTestHarness.Eventually(() => tab.Tokens.LatestTurns.Count == 1, "the tab's turns");
        var latest = tab.Tokens.LatestTurns[0];
        Assert.Equal(("Read the grapple code", "$0.04"), (latest.Prompt, latest.Cost));
        Assert.Equal("All 1 turn in the breakdown ›", tab.Tokens.AllTurnsText);
        Assert.Equal($"Last turn: {Formats.Tokens(42_225)} · $0.04", tab.Tokens.LastTurnText);
        Assert.Equal(["Tokens", "Cost", "Session limit", "From the cache"], tab.Tokens.Tiles.Select(t => t.Label));
        Assert.Single(tab.Tokens.Columns);
        Assert.Contains("about $0.04", tab.InfoRows.Single(r => r.Label == "Tokens").Value, StringComparison.Ordinal);

        tab.Tokens.OpenTurnCommand.Execute(latest);

        Assert.Equal((tab, latest.TurnId, (int?)1), asked);
    }

    [Fact]
    public async Task The_next_message_costs_a_read_of_the_cache_until_it_expires_and_a_write_after()
    {
        await using var h = new TabTestHarness();
        h.Transport.Answers["get_context_usage"] = _ => new JsonObject
        {
            ["totalTokens"] = 40_000, ["maxTokens"] = 200_000, ["percentage"] = 20, ["autoCompactThreshold"] = 167_000, ["isAutoCompactEnabled"] = true,
        };
        var tab = await h.OpenTabAsync();
        tab.Tokens.SetWanted("window", true);
        await SendTurnAsync(h, tab, "Read it", apiKeySource: "none");

        await TabTestHarness.Eventually(() => tab.Tokens.ShowsNextMessage, "the next message");
        // Opus's price from the turn: $0.04 for 5,510 tokens weighed as input, its writes as five minutes'.
        const double price = 0.04 / 5_510;
        Assert.Equal(("Before 13:00", Formats.Dollars(40_000 * price * 0.1)), (tab.Tokens.NextBefore, tab.Tokens.NextWarmCost));
        Assert.Equal(("After it", Formats.Dollars(40_000 * price * 2)), (tab.Tokens.NextAfter, tab.Tokens.NextColdCost));
        Assert.False(tab.Tokens.IsCacheExpired);
        Assert.Equal($"{Formats.Tokens(40_000)} of {Formats.Tokens(200_000)} · compacts at {Formats.Tokens(167_000)}", tab.Tokens.ContextText);
        Assert.Equal(["In the context", "Free", "Auto-compact buffer"], tab.Tokens.ContextSegments.Select(s => s.Name));

        await TabTestHarness.Eventually(() => tab.Tokens.IsCacheExpired, "the cache expired", poll: () => h.Time.Advance(TimeSpan.FromMinutes(5)));

        Assert.Equal("Now", tab.Tokens.NextAfter);
        Assert.StartsWith("The cache expired at 13:00", tab.Tokens.NextNote, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sends <paramref name="prompt"/> and answers it as <see cref="TokenBreakdownWindowTests"/> does: a Read, Claude's
    /// answer, and Claude Code's own title on Haiku. 42,225 tokens and $0.0413 in all.
    /// </summary>
    private static async Task SendTurnAsync(TabTestHarness h, TabViewModel tab, string prompt, int calls = 3, string? apiKeySource = null)
    {
        var turns = tab.Items.OfType<TurnSummaryItem>().Count();
        tab.ComposerText = prompt;
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "system", ["subtype"] = "init", ["session_id"] = "s1", ["model"] = Opus, ["permissionMode"] = "default", ["apiKeySource"] = apiKeySource,
        });
        h.Transport.Emit(Assistant($"msg_{turns}_1", input: 3, output: 40, cacheRead: 20_000, write1h: 300, new JsonObject
        {
            ["type"] = "tool_use", ["id"] = $"toolu_{turns}", ["name"] = "Read", ["input"] = new JsonObject { ["file_path"] = "a.txt" },
        }));
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = $"toolu_{turns}", ["content"] = new string('x', 800) }),
            },
        });
        h.Transport.Emit(Assistant($"msg_{turns}_2", input: 2, output: 120, cacheRead: 20_300, write1h: 240, new JsonObject { ["type"] = "text", ["text"] = "Done." }));
        // Claude Code's modelUsage counts every turn of the process.
        h.Transport.Emit(Result(turns + 1));
        await TabTestHarness.Eventually(() => tab.IsSettled && tab.Items.OfType<TurnSummaryItem>().Count() == turns + 1
            && (h.Services.Usage is null || h.Services.Usage.Store.GetCalls(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, tab.Id).Count == calls), "the turn");
    }

    private static JsonObject Result(int turns = 1) => new()
    {
        ["type"] = "result",
        ["subtype"] = "success",
        ["is_error"] = false,
        ["result"] = "Done.",
        ["session_id"] = "s1",
        ["modelUsage"] = new JsonObject
        {
            [Opus] = Usage(input: 5 * turns, output: 160 * turns, cacheRead: 40_300 * turns, cacheWrite: 540 * turns, cost: 0.04 * turns),
            ["claude-haiku-5-5"] = Usage(input: 1_200 * turns, output: 20 * turns, cacheRead: 0, cacheWrite: 0, cost: 0.0013 * turns),
        },
    };

    private static JsonObject Assistant(string id, long input, long output, long cacheRead, long write1h, JsonObject content) => new()
    {
        ["type"] = "assistant",
        ["message"] = new JsonObject
        {
            ["id"] = id,
            ["model"] = Opus,
            ["content"] = new JsonArray(content),
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = input,
                ["output_tokens"] = output,
                ["cache_read_input_tokens"] = cacheRead,
                ["cache_creation_input_tokens"] = write1h,
                ["cache_creation"] = new JsonObject { ["ephemeral_5m_input_tokens"] = 0, ["ephemeral_1h_input_tokens"] = write1h },
            },
        },
    };

    private static JsonObject Usage(long input, long output, long cacheRead, long cacheWrite, double cost) => new()
    {
        ["inputTokens"] = input,
        ["outputTokens"] = output,
        ["cacheReadInputTokens"] = cacheRead,
        ["cacheCreationInputTokens"] = cacheWrite,
        ["costUSD"] = cost,
    };
}
