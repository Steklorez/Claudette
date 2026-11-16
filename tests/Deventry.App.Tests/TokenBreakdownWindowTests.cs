using System.Text.Json.Nodes;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Usage;

namespace Deventry.App.Tests;

/// <summary>A tab's token breakdown in its window (DESIGN.md §18, "The window", "Opening it").</summary>
public class TokenBreakdownWindowTests
{
    private const string Opus = "claude-opus-5-5";

    [Fact]
    public async Task The_tabs_menu_entry_asks_the_shell_for_its_window()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        ITokenBreakdownSubject? asked = null;
        h.Shell.TokenBreakdownRequested += (t, _, _) => asked = t;

        tab.ShowTokenBreakdownCommand.Execute(null);

        Assert.Same(tab, Assert.IsType<OpenTabBreakdown>(asked).Tab);
    }

    [Fact]
    public async Task A_turn_shows_with_its_prompt_its_calls_and_the_results_between_them()
    {
        await using var h = new TabTestHarness();
        var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        var tab = await h.OpenTabAsync();
        using var breakdown = new TokenBreakdownViewModel(h.Services, tab);
        breakdown.Refresh();
        await TabTestHarness.Eventually(() => breakdown.IsLoaded, "the empty breakdown");
        Assert.True(breakdown.IsEmpty);

        await SendTurnAsync(h, tab, "Read the grapple code\nand say what's wrong");
        // Read again as the tab's turn is recorded.
        await TabTestHarness.Eventually(() => breakdown.Turns.Count == 1, "the turn");

        Assert.Equal($"Token breakdown · {tab.DisplayName}", breakdown.Title);
        var turn = breakdown.Turns[0];
        Assert.Equal(("1", "Read the grapple code", "2"), (turn.NumberText, turn.Prompt, turn.CallsText));
        Assert.Equal(["Tokens", "Estimated cost", "Session limit", "From the cache", "Model calls", "Tool calls"], breakdown.Tiles.Select(t => t.Label));
        Assert.Equal("42.2k", breakdown.Tiles[0].Value);
        Assert.Equal("3", breakdown.Tiles[4].Value);
        Assert.Equal(["Cache read", "Cache write", "Fresh input", "Output"], breakdown.Kinds.Select(k => k.Name));
        Assert.Single(breakdown.Columns);

        turn.IsExpanded = true;
        Assert.Equal(
        [
            "Claude chose its next step",
            "↳ Read · its result added 200, read again by 1 later call",
            "Claude's answer",
            "Claude Code's own calls, which it doesn't show",
        ], turn.Calls.Select(c => c.What));
        Assert.Equal(["c1", "", "c2", ""], turn.Calls.Select(c => c.Label));
    }

    [Fact]
    public async Task Cost_is_remembered_and_shows_the_bars_in_dollars()
    {
        await using var h = new TabTestHarness();
        var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        var tab = await h.OpenTabAsync();
        await SendTurnAsync(h, tab, "Read it");
        using var breakdown = new TokenBreakdownViewModel(h.Services, tab);
        breakdown.Refresh();
        await TabTestHarness.Eventually(() => breakdown.Turns.Count == 1, "the turn");
        var tokens = breakdown.Turns[0].Values;

        breakdown.ShowCost = true;

        Assert.True(h.Services.State.TokensAsCost);
        Assert.Equal("Estimated cost per turn", breakdown.ChartUnit);
        Assert.NotEqual(tokens, breakdown.Turns[0].Values);
        Assert.Equal(0.0413, breakdown.Turns[0].Values.Total, 9);
        using var another = new TokenBreakdownViewModel(h.Services, tab);
        Assert.True(another.ShowCost);
    }

    [Fact]
    public async Task Showing_a_turn_selects_it_and_opens_its_calls()
    {
        await using var h = new TabTestHarness();
        var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        var tab = await h.OpenTabAsync();
        await SendTurnAsync(h, tab, "Read it");
        using var breakdown = new TokenBreakdownViewModel(h.Services, tab);
        breakdown.Refresh();
        await TabTestHarness.Eventually(() => breakdown.Turns.Count == 1, "the turn");
        TokenTurnRow? shown = null;
        breakdown.TurnShown += row => shown = row;

        breakdown.ShowTurnCommand.Execute(1);

        Assert.Equal(1, breakdown.SelectedTurn);
        Assert.Same(breakdown.Turns[0], shown);
        Assert.True(breakdown.Turns[0].IsSelected);
        Assert.True(breakdown.Turns[0].IsExpanded);
    }

    [Fact]
    public async Task A_closed_tabs_older_turns_get_their_calls_and_prompts_from_the_transcript_once()
    {
        await using var h = new TabTestHarness();
        var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        var at = h.Time.GetUtcNow().AddHours(-2);
        // Recorded before calls were, with Claude Code's title on Haiku besides its two calls.
        tracker.Store.AddTurns([new TurnRecord(at.AddSeconds(8), "tab-closed", "s1", Opus, 8, 160, 540, 40_300, 0.04), new TurnRecord(at.AddSeconds(8), "tab-closed", "s1", "claude-haiku-5-5", 1_200, 20, 0, 0, 0.0013)]);
        var projects = Path.Combine(h.Root, "projects");
        Directory.CreateDirectory(Path.Combine(projects, "work"));
        File.WriteAllLines(Path.Combine(projects, "work", "s1.jsonl"),
        [
            Entry(new JsonObject { ["type"] = "user", ["message"] = new JsonObject { ["role"] = "user", ["content"] = "Read the grapple code\nand say what's wrong" } }, at),
            Entry(Assistant("msg_1", input: 3, output: 40, cacheRead: 20_000, write1h: 300, new JsonObject { ["type"] = "tool_use", ["id"] = "toolu_1", ["name"] = "Read", ["input"] = new JsonObject() }), at.AddSeconds(2)),
            Entry(new JsonObject
            {
                ["type"] = "user",
                ["message"] = new JsonObject { ["role"] = "user", ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = "toolu_1", ["content"] = new string('x', 800) }) },
            }, at.AddSeconds(3)),
            Entry(Assistant("msg_2", input: 2, output: 120, cacheRead: 20_300, write1h: 240, new JsonObject { ["type"] = "text", ["text"] = "Done." }), at.AddSeconds(6)),
        ]);
        h.Services.ProjectsDirectory = projects;
        using var breakdown = new TokenBreakdownViewModel(h.Services, new HistoryTabBreakdown("tab-closed", "refactor auth", null, "api", "s1", UsesPlan: true));

        breakdown.Refresh();

        await TabTestHarness.Eventually(() => breakdown.Turns is [{ Turn.HasCalls: true }], "the turn's calls from the transcript");
        Assert.False(breakdown.IsReadingTranscript);
        Assert.Equal("Read the grapple code", breakdown.Turns[0].Prompt);
        Assert.Equal(["Turn 1 has its model calls now, read from the transcript, and agrees with the usage history."], breakdown.TranscriptLines);
        var calls = tracker.Store.GetCalls(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, "tab-closed");
        Assert.Equal(4, calls.Count);
        Assert.All(calls, c => Assert.Equal(CallSource.Transcript, c.Source));

        // Opened again, it's read for the prompts alone: nothing more to check, and nothing to say.
        using var again = new TokenBreakdownViewModel(h.Services, new HistoryTabBreakdown("tab-closed", "refactor auth", null, "api", "s1", UsesPlan: true));
        again.Refresh();
        await TabTestHarness.Eventually(() => again.Turns is [{ Prompt: "Read the grapple code" }], "the prompt again");
        Assert.Empty(again.TranscriptLines);
        Assert.Equal(4, tracker.Store.GetCalls(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, "tab-closed").Count);
    }

    [Fact]
    public void How_the_check_went_is_said_turn_by_turn()
    {
        Assert.Equal("Turn 4", TokenBreakdownViewModel.TurnList([4]));
        Assert.Equal("Turns 3 and 4", TokenBreakdownViewModel.TurnList([4, 3]));
        Assert.Equal("turns 1–6", TokenBreakdownViewModel.TurnList([1, 2, 3, 4, 5, 6], lower: true));
        Assert.Equal("Turns 1, 3 and 5–7", TokenBreakdownViewModel.TurnList([1, 3, 5, 6, 7]));

        Assert.Equal(["The transcript of turns 1 and 2 isn't there: Claude Code deletes old ones. They keep their totals without calls."],
            TokenBreakdownViewModel.Describe([], [1, 2], new Dictionary<string, string>(), promptsOnly: false));
    }

    private static string Entry(JsonObject entry, DateTimeOffset at)
    {
        entry["timestamp"] = at.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        return entry.ToJsonString();
    }

    [Fact]
    public void The_export_has_a_line_per_call_with_its_turns_prompt()
    {
        var at = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
        var breakdown = TokenBreakdown.From(
            [new TurnRecord(at.AddSeconds(5), "tab", "s1", Opus, 5, 160, 540, 40_300, 0.04) { Turn = "t1" }, new TurnRecord(at.AddMinutes(9), "tab", "s1", Opus, 1, 2, 3, 4, 0.01)],
            [
                new CallRecord("t1", "tab", "s1", at, 1, "msg_1", null, null, Opus, 3, 40, 20_000, 0, 300, 0, 0, Core.Sessions.CallKind.Call, 0.02),
                new CallRecord("t1", "tab", "s1", at.AddSeconds(2), 2, "msg_2", null, null, Opus, 2, 120, 20_300, 0, 240, 0, 0, Core.Sessions.CallKind.Call, 0.02),
            ], [], []);

        var csv = TokenBreakdownViewModel.ToCsv(breakdown, new Dictionary<TurnBreakdown, string> { [breakdown.Turns[0]] = "Read it, \"twice\"" });

        Assert.Equal(
        [
            "turn,ended,prompt,call,kind,agent,model,input,cache_read,cache_write_5m,cache_write_1h,output,cost_usd",
            $"1,2026-11-15T09:00:05Z,\"Read it, \"\"twice\"\"\",1,Call,main,{Opus},3,20000,0,300,40,0.02",
            $"1,2026-11-15T09:00:05Z,\"Read it, \"\"twice\"\"\",2,Call,main,{Opus},2,20300,0,240,120,0.02",
            $"2,2026-11-15T09:09:00Z,,,,,{Opus},1,4,3,,2,0.01",
        ], csv.TrimEnd('\n').Split('\n'));
    }

    /// <summary>
    /// Sends <paramref name="prompt"/> and answers it as Claude Code would: a Read, Claude's answer, and Claude Code's own
    /// title on Haiku, which the stream never shows. 42,225 tokens and $0.0413 in all.
    /// </summary>
    private static async Task SendTurnAsync(TabTestHarness h, TabViewModel tab, string prompt)
    {
        tab.ComposerText = prompt;
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.Emit(new JsonObject { ["type"] = "system", ["subtype"] = "init", ["session_id"] = "s1", ["model"] = Opus, ["permissionMode"] = "default" });
        h.Transport.Emit(Assistant("msg_1", input: 3, output: 40, cacheRead: 20_000, write1h: 300, new JsonObject
        {
            ["type"] = "tool_use", ["id"] = "toolu_1", ["name"] = "Read", ["input"] = new JsonObject { ["file_path"] = "a.txt" },
        }));
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = "toolu_1", ["content"] = new string('x', 800) }),
            },
        });
        h.Transport.Emit(Assistant("msg_2", input: 2, output: 120, cacheRead: 20_300, write1h: 240, new JsonObject { ["type"] = "text", ["text"] = "Done." }));
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "result",
            ["subtype"] = "success",
            ["is_error"] = false,
            ["result"] = "Done.",
            ["session_id"] = "s1",
            ["modelUsage"] = new JsonObject
            {
                [Opus] = Usage(input: 5, output: 160, cacheRead: 40_300, cacheWrite: 540, cost: 0.04),
                ["claude-haiku-5-5"] = Usage(input: 1_200, output: 20, cacheRead: 0, cacheWrite: 0, cost: 0.0013),
            },
        });
        await TabTestHarness.Eventually(() => tab.IsSettled && h.Services.Usage!.Store.GetCalls(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, tab.Id).Count == 3, "the turn's calls");
    }

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
