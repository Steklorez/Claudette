using System.Globalization;
using System.Text.Json.Nodes;
using Deventry.Core.Sessions;
using Deventry.Core.Transcripts;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Usage.Tests;

/// <summary>
/// A tab's recorded turns checked against their session's transcript (DESIGN.md §18, "Older turns: reading the
/// transcript"): calls added to turns recorded without them, corrected where they don't agree, turns the history missed
/// added, and the check kept so a transcript is read once.
/// </summary>
public sealed class TurnCheckTests : IDisposable
{
    private const string Opus = "claude-opus-5-5";
    private const string Haiku = "claude-haiku-5-5";
    private const string Tab = "tab-1";
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"deventry-turn-check-{Guid.NewGuid():N}");
    private readonly List<UsageStore> _stores = [];

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void A_turn_recorded_without_calls_gets_the_transcripts_with_claude_codes_own_as_the_rest()
    {
        // Two calls on Opus in the transcript; the turn's record has them and Claude Code's title on Haiku.
        var transcript = Turns(Prompt("Read the grapple code", T0), Call("msg_1", T0.AddSeconds(2), read: 20_000, write: 300, tool: ("toolu_1", "Read")),
            Result("toolu_1", 800, T0.AddSeconds(3)), Call("msg_2", T0.AddSeconds(6), read: 20_300, write: 240));
        TurnRecord[] records = [Record(T0.AddSeconds(8), Opus, input: 8, output: 160, write: 540, read: 40_300, cost: 0.04), Record(T0.AddSeconds(8), Haiku, input: 1_200, cost: 0.0013)];

        var check = Check(records, transcript);

        var turn = Assert.Single(check.Turns);
        Assert.Equal(TurnCheckOutcome.Agreed, turn.Outcome);
        Assert.NotNull(turn.Turn);
        // What the record has beyond them is Claude Code's own, a call per model.
        Assert.Equal([("msg_1", CallKind.Call), ("msg_2", CallKind.Call), ((string?)null, CallKind.ClaudeCode), (null, CallKind.ClaudeCode)],
            turn.Detail.Calls.Select(c => (c.MessageId, c.Kind)));
        Assert.Equal([Opus, Opus, Opus, Haiku], turn.Detail.Calls.Select(c => c.Model));
        Assert.All(turn.Detail.Calls, c => Assert.Equal(CallSource.Transcript, c.Source));
        // The calls add up to the records, cost too.
        Assert.Equal(records.Sum(r => r.Total), turn.Detail.Calls.Sum(c => c.Total));
        Assert.Equal(records.Sum(r => r.CostUsd), turn.Detail.Calls.Sum(c => c.CostUsd), 9);
        Assert.Equal(("Read", 200L), (Assert.Single(turn.Detail.Steps).Tool, turn.Detail.Steps[0].Tokens));
        Assert.Equal((long)TurnCheckOutcome.Agreed, turn.Detail.Events.Single(e => e.Kind == UsageEventKind.Checked).Value);
        Assert.Equal("Read the grapple code", check.Prompts[turn.Turn]);
    }

    [Fact]
    public void A_turn_that_counted_earlier_turns_again_is_corrected_to_the_transcript_at_its_models_price()
    {
        // Recorded before Deventry worked out each turn's part of the running totals: ten times what it sent.
        var transcript = Turns(Prompt("Go on", T0), Call("msg_1", T0.AddSeconds(2), read: 20_000, write: 300));
        var record = Record(T0.AddSeconds(4), Opus, input: 30, output: 500, write: 3_000, read: 200_000, cost: 0.30);

        var check = Check([record], transcript);

        var turn = Assert.Single(check.Turns);
        Assert.Equal(TurnCheckOutcome.Corrected, turn.Outcome);
        var corrected = Assert.Single(turn.Records);
        Assert.Equal((3L, 50L, 300L, 20_000L), (corrected.Input, corrected.Output, corrected.CacheWrite, corrected.CacheRead));
        // Its cost by the price the record had, per weighted token: the record's writes as five minutes', the call's an hour's.
        var weight = (3 + (20_000 * 0.1) + (300 * 2.0) + (50 * 5.0)) / (30 + (200_000 * 0.1) + (3_000 * 1.25) + (500 * 5.0));
        Assert.Equal(0.30 * weight, corrected.CostUsd, 9);
        Assert.Equal(record, Assert.Single(turn.Before));
        Assert.Contains("203530 tokens", turn.Detail.Events.Single(e => e.Kind == UsageEventKind.Checked).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_turn_between_recorded_ones_that_the_history_missed_is_added()
    {
        var transcript = Turns(
            Prompt("One", T0), Call("msg_1", T0.AddSeconds(2), read: 10_000, write: 300),
            Prompt("Two, cut short", T0.AddMinutes(5)), Call("msg_2", T0.AddMinutes(5).AddSeconds(2), read: 10_300, write: 200),
            Prompt("Three", T0.AddMinutes(30)), Call("msg_3", T0.AddMinutes(30).AddSeconds(2), read: 10_500, write: 100));
        TurnRecord[] records =
        [
            Record(T0.AddSeconds(4), Opus, input: 3, output: 50, write: 300, read: 10_000, cost: 0.01) with { Index = 1 },
            Record(T0.AddMinutes(30).AddSeconds(4), Opus, input: 3, output: 50, write: 100, read: 10_500, cost: 0.01) with { Index = 2 },
        ];

        var check = Check(records, transcript);

        var added = Assert.Single(check.Turns, t => t.Outcome == TurnCheckOutcome.Added);
        Assert.Equal(1, added.Number);
        var record = Assert.Single(added.Records);
        Assert.Equal((10_300L, 200L, (int?)null), (record.CacheRead, record.CacheWrite, record.Index));
        // At the model's price from the tab's other turns.
        Assert.True(record.CostUsd > 0);
        Assert.Equal(2, check.Turns.Count(t => t.Outcome == TurnCheckOutcome.Agreed));
    }

    [Fact]
    public void Before_the_tabs_first_turn_and_after_its_last_the_transcripts_turns_arent_the_tabs()
    {
        var transcript = Turns(
            Prompt("Run outside Deventry", T0), Call("msg_0", T0.AddSeconds(2), read: 9_000, write: 300),
            Prompt("One", T0.AddMinutes(10)), Call("msg_1", T0.AddMinutes(10).AddSeconds(2), read: 10_000, write: 300),
            Prompt("Running now", T0.AddMinutes(20)), Call("msg_2", T0.AddMinutes(20).AddSeconds(2), read: 10_300, write: 200));

        var check = Check([Record(T0.AddMinutes(10).AddSeconds(4), Opus, input: 3, output: 50, write: 300, read: 10_000, cost: 0.01)], transcript);

        Assert.Equal([TurnCheckOutcome.Agreed], check.Turns.Select(t => t.Outcome));
    }

    [Fact]
    public void Another_machines_turn_is_checked_but_kept_as_it_recorded_it_and_a_turn_checked_before_is_only_read_past()
    {
        var transcript = Turns(
            Prompt("One", T0), Call("msg_1", T0.AddSeconds(2), read: 10_000, write: 300),
            Prompt("Two", T0.AddMinutes(5)), Call("msg_2", T0.AddMinutes(5).AddSeconds(2), read: 10_300, write: 200));
        var store = Open();
        store.AddTurns([Record(T0.AddSeconds(4), Opus, input: 3, output: 50, write: 300, read: 10_000, cost: 0.01) with { Turn = "t1" }],
            new TurnDetail([], [], [new UsageEvent(Tab, "s1", "t1", T0.AddSeconds(4), UsageEventKind.Checked, (long)TurnCheckOutcome.Agreed)]));
        var theirs = Record(T0.AddMinutes(5).AddSeconds(4), Opus, input: 30, output: 500, write: 3_000, read: 200_000, cost: 0.3) with { Turn = "t2", Machine = "lap" };

        var check = TurnCheck.Check("s1", [.. TokenBreakdown.From([.. store.GetTurns(DateTimeOffset.MinValue, DateTimeOffset.MaxValue), theirs], [], [],
            store.GetEvents(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).Turns], transcript, TokenPrices.From([], []), NewId);

        var turn = Assert.Single(check.Turns);
        Assert.Equal((TurnCheckOutcome.Differs, "lap"), (turn.Outcome, turn.Machine));
        Assert.Equal(theirs, Assert.Single(turn.Records));
        Assert.Empty(turn.Detail.Calls);
    }

    [Fact]
    public void A_check_taken_in_keeps_each_records_id_marks_its_calls_and_numbers_an_added_turn_in_its_place()
    {
        var store = Open();
        var transcript = Turns(
            Prompt("One", T0), Call("msg_1", T0.AddSeconds(2), read: 10_000, write: 300),
            Prompt("Two, cut short", T0.AddMinutes(5)), Call("msg_2", T0.AddMinutes(5).AddSeconds(2), read: 10_300, write: 200),
            Prompt("Three", T0.AddMinutes(30)), Call("msg_3", T0.AddMinutes(30).AddSeconds(2), read: 10_500, write: 100));
        // The first recorded before ids, and too much; the last with its number.
        store.AddTurns([Record(T0.AddSeconds(4), Opus, input: 30, output: 500, write: 3_000, read: 100_000, cost: 0.2)]);
        store.AddTurns([Record(T0.AddMinutes(30).AddSeconds(4), Opus, input: 3, output: 50, write: 100, read: 10_500, cost: 0.01) with { Turn = "t3", Index = 2 }]);
        var shared = store.GetSessionUsage("s1", "desk", "Desktop").Turns.Select(t => t.Id).ToHashSet();

        var check = TurnCheck.Check("s1", [.. TokenBreakdown.Read(store, Tab, DateTimeOffset.MinValue, DateTimeOffset.MaxValue).Turns], transcript,
            TokenPrices.From([], store.GetTurns(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)), NewId);
        store.ApplyCheck(Tab, check.Turns);

        var read = TokenBreakdown.Read(store, Tab, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        Assert.Equal([(1, TurnCheckOutcome.Corrected), (2, TurnCheckOutcome.Added), (3, TurnCheckOutcome.Agreed)], read.Turns.Select(t => (t.Number, t.Check!.Value)));
        Assert.All(read.Turns, t => Assert.NotNull(t.Turn));
        Assert.True(read.Turns[0].FromTranscript);
        Assert.Equal(10_000, read.Turns[0].Records.Single().CacheRead);
        // A turn recorded before its calls has its idle time from the transcript.
        Assert.Equal(TimeSpan.FromMinutes(25), read.Turns[2].IdleBefore);
        // A record corrected in place keeps its id across machines; only the added turn's is new.
        var after = store.GetSessionUsage("s1", "desk", "Desktop").Turns.Select(t => t.Id).ToHashSet();
        Assert.Superset(shared, after);
        Assert.Single(after.Except(shared));

        // Read once: checked again, nothing changes.
        var again = TurnCheck.Check("s1", [.. read.Turns], transcript, read.Prices, NewId);
        Assert.Empty(again.Turns);
    }

    [Fact]
    public void A_transcript_turn_from_another_time_or_without_a_live_turns_calls_isnt_taken()
    {
        // The transcript's only turn ran an hour before the first recorded one ended: whatever it was, it isn't that turn.
        var transcript = Turns(Prompt("Earlier", T0), Call("msg_1", T0.AddSeconds(2), read: 500_000, write: 300));
        var older = Record(T0.AddHours(1), Opus, input: 3, output: 50, write: 300, read: 10_000, cost: 0.01);
        // And one recorded live, whose calls the transcript doesn't have.
        var live = Record(T0.AddHours(1).AddMinutes(1), Opus, input: 3, output: 50, write: 300, read: 10_000, cost: 0.01) with { Turn = "t-live" };
        CallRecord[] calls = [new("t-live", Tab, "s1", T0.AddHours(1).AddSeconds(50), 1, "msg_other", null, null, Opus, 3, 50, 10_000, 0, 300, 0, 0, CallKind.Call, 0.01)];

        var check = TurnCheck.Check("s1", [.. TokenBreakdown.From([older, live], calls, [], []).Turns], transcript, TokenPrices.From([], []), NewId);

        Assert.Equal([TurnCheckOutcome.NotInTranscript, TurnCheckOutcome.NotInTranscript], check.Turns.Select(t => t.Outcome));
        Assert.All(check.Turns, t => Assert.Empty(t.Detail.Calls));
        Assert.Equal(older with { Turn = check.Turns[0].Turn }, Assert.Single(check.Turns[0].Records));
        Assert.Equal(live, Assert.Single(check.Turns[1].Records));
    }

    [Fact]
    public void A_compaction_in_the_transcript_is_the_rest_of_its_turn_and_its_summary_doesnt_start_another()
    {
        var lines = new[]
        {
            Prompt("Keep going", T0),
            Call("msg_1", T0.AddSeconds(2), read: 150_000, write: 300),
            Line(new JsonObject { ["type"] = "system", ["subtype"] = "compact_boundary", ["timestamp"] = Stamp(T0.AddSeconds(30)), ["compactMetadata"] = new JsonObject { ["trigger"] = "auto", ["preTokens"] = 150_300 } }),
            Line(new JsonObject { ["type"] = "user", ["isCompactSummary"] = true, ["timestamp"] = Stamp(T0.AddSeconds(31)), ["message"] = new JsonObject { ["role"] = "user", ["content"] = "This session is being continued…" } }),
            Call("msg_2", T0.AddSeconds(40), read: 0, write: 12_000),
        };
        var transcript = TranscriptTurns.Of(TranscriptReader.Read(lines));
        // The compaction's own call read the context and wrote its summary: the record's rest.
        var record = Record(T0.AddSeconds(45), Opus, input: 6, output: 4_100, write: 12_300, read: 300_300, cost: 0.5);

        var check = Check([record], transcript);

        Assert.Single(transcript);
        var turn = Assert.Single(check.Turns);
        Assert.Equal(TurnCheckOutcome.Agreed, turn.Outcome);
        Assert.Equal(CallKind.Compaction, turn.Detail.Calls[^1].Kind);
        Assert.Equal((UsageEventKind.Compacted, 150_300L), (turn.Detail.Events[0].Kind, turn.Detail.Events[0].Value!.Value));
    }

    private static SessionCheck Check(IReadOnlyList<TurnRecord> records, IReadOnlyList<TranscriptTurn> transcript) =>
        TurnCheck.Check("s1", [.. TokenBreakdown.From(records, [], [], []).Turns], transcript, TokenPrices.From([], records), NewId);

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static TurnRecord Record(DateTimeOffset at, string model, long input = 0, long output = 0, long write = 0, long read = 0, double cost = 0) =>
        new(at, Tab, "s1", model, input, output, write, read, cost);

    private static IReadOnlyList<TranscriptTurn> Turns(params string[] lines) => TranscriptTurns.Of(TranscriptReader.Read(lines));

    private static string Prompt(string text, DateTimeOffset at) =>
        Line(new JsonObject { ["type"] = "user", ["timestamp"] = Stamp(at), ["message"] = new JsonObject { ["role"] = "user", ["content"] = text } });

    private static string Call(string id, DateTimeOffset at, long read, long write, (string Id, string Name)? tool = null) => Line(new JsonObject
    {
        ["type"] = "assistant",
        ["timestamp"] = Stamp(at),
        ["message"] = new JsonObject
        {
            ["id"] = id,
            ["model"] = Opus,
            ["content"] = tool is { } use
                ? new JsonArray(new JsonObject { ["type"] = "tool_use", ["id"] = use.Id, ["name"] = use.Name, ["input"] = new JsonObject() })
                : new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "Done." }),
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = 3,
                ["output_tokens"] = 50,
                ["cache_read_input_tokens"] = read,
                ["cache_creation_input_tokens"] = write,
                ["cache_creation"] = new JsonObject { ["ephemeral_5m_input_tokens"] = 0, ["ephemeral_1h_input_tokens"] = write },
            },
        },
    });

    private static string Result(string toolUseId, int characters, DateTimeOffset at) => Line(new JsonObject
    {
        ["type"] = "user",
        ["timestamp"] = Stamp(at),
        ["message"] = new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = toolUseId, ["content"] = new string('x', characters) }),
        },
    });

    private static string Stamp(DateTimeOffset at) => at.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string Line(JsonObject entry) => entry.ToJsonString();

    private UsageStore Open()
    {
        Directory.CreateDirectory(_root);
        var store = new UsageStore(Path.Combine(_root, $"usage-{Guid.NewGuid():N}.db"), new FakeTimeProvider(T0.AddHours(1)));
        _stores.Add(store);
        return store;
    }
}
