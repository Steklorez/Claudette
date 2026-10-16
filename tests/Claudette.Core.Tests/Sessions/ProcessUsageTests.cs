using System.Text.Json.Nodes;
using Claudette.Core.Protocol;
using Claudette.Core.Sessions;
using Claudette.Core.Tests.Support;
using Claudette.Core.Transcripts;
using Microsoft.Extensions.Time.Testing;

namespace Claudette.Core.Tests.Sessions;

/// <summary>
/// A <c>result</c>'s <c>modelUsage</c> is the process's running total, so each turn's usage is what it added to the
/// result before (DESIGN.md §6, "Usage history").
/// </summary>
public class ProcessUsageTests
{
    // ---- Recorded traffic ------------------------------------------------------------------------------------------

    [Fact]
    public void Turns_of_one_process_add_up_to_its_last_result_and_not_to_the_sum_of_its_results()
    {
        // Four turns and an interrupt in one process, with a model change after the first: each result repeats what came
        // before. Summed as they come they'd be 0.0308; Claude Code's own total is 0.0088.
        var turns = Replay("02-control-protocol");

        Assert.Equal(0.0088, turns.Sum(t => t.CostUsd), 6);
        Assert.Equal(["claude-haiku-4-5", "claude-sonnet-5-5", "claude-sonnet-5-5", "claude-sonnet-5-5"], turns.Where(t => !t.IsEmpty).Select(t => Assert.Single(t.Models).Key));
        Assert.All(turns.Where(t => !t.IsEmpty).Skip(1), t => Assert.Equal(1000, t.Models.Values.Single().Input));
        // The interrupted turn's result counts nothing new.
        Assert.True(turns[^1].IsEmpty);
    }

    [Theory]
    [MemberData(nameof(Versions), "09-clear")]
    public void Clearing_the_conversation_starts_the_totals_again(string version)
    {
        // A turn, /clear (whose result is {}), another turn: each turn counts once.
        var turns = Replay("09-clear", version);

        Assert.Equal([0.0011, 0, 0.0011], turns.Select(t => Math.Round(t.CostUsd, 6)));
        Assert.Equal(2000, turns.Sum(t => t.Models.Values.Sum(m => m.Input)));
    }

    [Theory]
    [MemberData(nameof(Versions), "10-compact")]
    public void Compacting_counts_only_the_compaction_call(string version)
    {
        // /compact doesn't start the totals again: its result counts the turn before it and the compaction call.
        var turns = Replay("10-compact", version);

        Assert.Equal(2, turns.Count);
        Assert.Equal([1000, 1000], turns.Select(t => t.Models.Values.Single().Input));
        Assert.Equal(0.0022, turns.Sum(t => t.CostUsd), 6);
    }

    [Fact]
    public void A_turns_subagents_are_counted_with_it()
    {
        // usage covers only the main loop; modelUsage has the subagents too.
        var turn = Assert.Single(Replay("12-subagents"));

        Assert.Equal(7000, turn.Models.Values.Sum(m => m.Input));
    }

    public static TheoryData<string> Versions(string name) =>
        [.. ProtocolFixture.AllVersions().Where(v => File.Exists(Path.Combine(AppContext.BaseDirectory, "Fixtures", "protocol", v, $"{name}.jsonl")))];

    /// <summary>Each result's turn, as a session works it out from the fixture's messages.</summary>
    private static List<ModelUsageTotals> Replay(string name, string version = ProtocolFixture.DefaultVersion)
    {
        var usage = new ProcessUsage();
        var turns = new List<ModelUsageTotals>();
        foreach (var line in ProtocolFixture.Load(name, version).OutputLines)
        {
            Assert.True(MessageParser.TryParse(line, out var message, out _));
            switch (message)
            {
                case ResultMessage result:
                    turns.Add(usage.Turn(result));
                    break;
                case ConversationResetMessage:
                    usage.Reset();
                    break;
            }
        }
        return turns;
    }

    // ---- Resuming --------------------------------------------------------------------------------------------------

    [Fact]
    public void A_resumed_session_counts_only_what_its_first_turn_added_to_the_restored_totals()
    {
        var usage = new ProcessUsage([Totals(("opus", 5000, 0.50))]);

        var first = usage.Turn(Result(("opus", 6000, 0.60)));
        var second = usage.Turn(Result(("opus", 7000, 0.70)));

        Assert.Equal((1000, 0.10), (first.Models["opus"].Input, Math.Round(first.CostUsd, 6)));
        Assert.Equal((1000, 0.10), (second.Models["opus"].Input, Math.Round(second.CostUsd, 6)));
    }

    [Fact]
    public void The_latest_saved_totals_the_first_result_could_have_grown_from_are_taken()
    {
        // Saved at three exits; resumed at an earlier point, Claude Code started from the second.
        var usage = new ProcessUsage([Totals(("opus", 1000, 0.1)), Totals(("opus", 3000, 0.3)), Totals(("opus", 9000, 0.9))]);

        var turn = usage.Turn(Result(("opus", 4000, 0.4)));

        Assert.Equal(1000, turn.Models["opus"].Input);
    }

    [Fact]
    public void Saved_totals_the_first_result_couldnt_have_grown_from_are_dropped()
    {
        // A model the first result doesn't have: Claude Code didn't start from these totals.
        var usage = new ProcessUsage([Totals(("opus", 1000, 0.1), ("haiku", 10, 0.001))]);

        var turn = usage.Turn(Result(("opus", 1500, 0.15)));

        Assert.Equal(1500, turn.Models["opus"].Input);
    }

    [Fact]
    public void Results_with_nothing_counted_leave_the_totals_where_they_were()
    {
        // A crash's result may be zeroed, and /clear's own is {}.
        var usage = new ProcessUsage();
        usage.Turn(Result(("opus", 1000, 0.1)));

        Assert.True(usage.Turn(Result(("opus", 0, 0))).IsEmpty);
        Assert.True(usage.Turn(Result()).IsEmpty);
        Assert.Equal(500, usage.Turn(Result(("opus", 1500, 0.15))).Models["opus"].Input);
    }

    [Fact]
    public async Task A_session_reports_each_turns_share_of_the_running_totals()
    {
        await using var transport = new FakeTransport();
        await using var session = new ClaudeSession(transport, new FakeTimeProvider(), restoredUsage: [Totals(("opus", 1000, 0.1))]);
        await session.InitializeAsync(TestContext.Current.CancellationToken);

        transport.Emit(Result(("opus", 1200, 0.12)).Raw.ToJsonString());
        var (first, _) = await session.ReadUntilAsync<TurnCompleted>();
        transport.Emit("""{"type":"conversation_reset","new_conversation_id":"s2","trigger":"clear"}""");
        transport.Emit(Result(("opus", 300, 0.03)).Raw.ToJsonString());
        var (second, _) = await session.ReadUntilAsync<TurnCompleted>();

        Assert.Equal(200, first.Usage.Models["opus"].Input);
        Assert.Equal(300, second.Usage.Models["opus"].Input);
    }

    private static ModelUsageTotals Totals(params (string Model, long Input, double Cost)[] models) => ModelUsageTotals.From(ModelUsage(models));

    private static ResultMessage Result(params (string Model, long Input, double Cost)[] models)
    {
        var raw = new JsonObject { ["type"] = "result", ["subtype"] = "success", ["is_error"] = false, ["session_id"] = "s1", ["modelUsage"] = ModelUsage(models) };
        Assert.True(MessageParser.TryParse(raw.ToJsonString(), out var message, out _));
        return (ResultMessage)message!;
    }

    private static JsonObject ModelUsage((string Model, long Input, double Cost)[] models)
    {
        var usage = new JsonObject();
        foreach (var (model, input, cost) in models)
        {
            usage[model] = new JsonObject { ["inputTokens"] = input, ["outputTokens"] = 0, ["costUSD"] = cost };
        }
        return usage;
    }

    // ---- The totals a transcript saved -------------------------------------------------------------------------------

    [Fact]
    public void A_transcripts_saved_totals_are_read_oldest_first()
    {
        var totals = CostState.Read(
        [
            """{"type":"user","message":{"role":"user","content":"what does cost-state mean?"}}""",
            """{"type":"cost-state","sessionId":"s1","totalCostUSD":0.1,"modelUsage":{"opus":{"inputTokens":1000,"outputTokens":10,"cacheReadInputTokens":0,"cacheCreationInputTokens":0,"costUSD":0.1}}}""",
            """{"type":"cost-state","sessionId":"s1",""",
            """{"type":"cost-state","sessionId":"s1","totalCostUSD":0.3,"modelUsage":{"opus":{"inputTokens":3000,"outputTokens":30,"costUSD":0.3},"haiku":{"inputTokens":5,"outputTokens":1,"costUSD":0.001}}}""",
        ]);

        Assert.Equal([1000, 3005], totals.Select(t => t.Models.Values.Sum(m => m.Input)));
        Assert.Equal(["haiku", "opus"], totals[1].Models.Keys.Order());
    }

    [Fact]
    public async Task A_transcript_without_saved_totals_or_that_isnt_there_has_none()
    {
        using var folder = new TempFolder("claudette-cost-state");
        var path = folder.Combine("s1.jsonl");
        await File.WriteAllTextAsync(path, """{"type":"user","message":{"role":"user","content":"hi"}}""" + "\n", TestContext.Current.CancellationToken);

        Assert.Empty(await CostState.ReadAllAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(await CostState.ReadAllAsync(folder.Combine("missing.jsonl"), TestContext.Current.CancellationToken));
    }
}
