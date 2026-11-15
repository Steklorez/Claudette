using Deventry.Core.Sessions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Usage.Tests;

/// <summary>Each turn's model calls, tools' results and events in the usage history (DESIGN.md §18, "What's recorded").</summary>
public sealed class CallRecordsTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"deventry-calls-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _time = new(Start);
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

    private string DatabasePath => Path.Combine(_root, "usage.db");

    [Fact]
    public void A_turn_is_recorded_with_its_calls_tools_results_and_compactions()
    {
        var store = Open();
        var log = new TurnCallLog(
        [
            new ModelCall(1, "msg_1", null, null, "opus", Start, new CallTokens(3, 120, 40_000, 0, 500), 1, 2, CallKind.CacheExpired, 0.30),
            new ModelCall(2, "msg_2", "toolu_agent", "Explore", "haiku", Start.AddSeconds(4), new CallTokens(5, 20, 0, 3_000, 0), 0, 0, CallKind.Call, 0.01),
            new ModelCall(3, null, null, null, "opus", Start.AddSeconds(9), new CallTokens(7, 20_000, 964_000, 0, 0), 0, 0, CallKind.Compaction, 0.98),
        ],
        [
            new ToolStep(1, null, "Read", 2_048, false, Start.AddSeconds(1)),
            new ToolStep(2, "toolu_agent", "Grep", 4, true, Start.AddSeconds(5)),
        ],
        [new Compaction(Start.AddSeconds(8), "auto", 967_045)],
        TimeSpan.FromMinutes(72));

        RecordTurn(store, "turn-1", "tab-1", log);

        var turn = Assert.Single(store.GetTurns(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        Assert.Equal(("turn-1", TimeSpan.FromMinutes(72)), (turn.Turn, turn.IdleBefore));
        var calls = store.GetCalls(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        Assert.Equal(CallRecord.From(log, "turn-1", "tab-1", "session-1"), calls);
        Assert.Equal(CallSource.Live, calls[0].Source);
        Assert.Equal(StepRecord.From(log, "turn-1"), store.GetSteps(["turn-1"]));
        Assert.Equal(
            [new UsageEvent("tab-1", "session-1", "turn-1", Start.AddSeconds(8), UsageEventKind.Compacted, 967_045, "auto")],
            store.GetEvents(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
    }

    [Fact]
    public void Calls_and_events_are_read_by_range_and_tab_oldest_first()
    {
        var store = Open();
        RecordTurn(store, "turn-a", "tab-1", Log(Start));
        RecordTurn(store, "turn-b", "tab-2", Log(Start.AddMinutes(1)));
        RecordTurn(store, "turn-c", "tab-1", Log(Start.AddMinutes(2)));
        store.AddEvent(new UsageEvent("tab-1", "session-1", null, Start.AddMinutes(3), UsageEventKind.Cleared));

        Assert.Equal(["turn-a", "turn-a", "turn-c", "turn-c"], store.GetCalls(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, "tab-1").Select(c => c.Turn));
        Assert.Equal([1, 2], store.GetCalls(Start.AddMinutes(1), Start.AddMinutes(1).AddSeconds(30)).Select(c => c.Seq));
        Assert.Equal([UsageEventKind.Cleared], store.GetEvents(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, "tab-1").Select(e => e.Kind));
        Assert.Empty(store.GetEvents(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, "tab-2"));
        // Steps are read by turn, each turn's in order of its calls.
        Assert.Equal(["turn-a", "turn-c"], store.GetSteps(["turn-c", "turn-a", "turn-a"]).Select(s => s.Turn).Order());
    }

    [Fact]
    public void Calls_steps_and_events_go_with_their_turns_when_pruned_or_cleared()
    {
        var store = Open();
        RecordTurn(store, "old", "tab-1", Log(Start));
        store.AddEvent(new UsageEvent("tab-1", null, null, Start, UsageEventKind.Cleared));
        _time.Advance(TimeSpan.FromDays(2));
        RecordTurn(store, "new", "tab-1", Log(_time.GetUtcNow()));

        store.Prune(TimeSpan.FromDays(1));

        Assert.All(store.GetCalls(DateTimeOffset.MinValue, DateTimeOffset.MaxValue), c => Assert.Equal("new", c.Turn));
        Assert.Empty(store.GetSteps(["old"]));
        Assert.Single(store.GetSteps(["new"]));
        Assert.Empty(store.GetEvents(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));

        store.Clear();

        Assert.Empty(store.GetCalls(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        Assert.Empty(store.GetSteps(["new"]));
    }

    [Fact]
    public void Clearing_on_every_machine_takes_the_calls_up_to_then()
    {
        var store = Open();
        RecordTurn(store, "before", "tab-1", Log(Start));
        RecordTurn(store, "after", "tab-1", Log(Start.AddMinutes(10)));

        store.ClearThrough(Start.AddMinutes(5));

        Assert.All(store.GetCalls(DateTimeOffset.MinValue, DateTimeOffset.MaxValue), c => Assert.Equal("after", c.Turn));
        Assert.Empty(store.GetSteps(["before"]));
    }

    [Fact]
    public void A_history_from_before_calls_is_upgraded_and_its_turns_have_none()
    {
        var store = Open();
        store.AddTurns([new TurnRecord(Start, "tab-1", null, "opus", 1, 1, 1, 1, 0)]);
        store.Dispose();
        // Version 6 had no calls, steps or events, and no turn ids.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE calls; DROP TABLE steps; DROP TABLE events; DROP INDEX turns_by_turn;
                ALTER TABLE turns DROP COLUMN turn; ALTER TABLE turns DROP COLUMN idle_ms; PRAGMA user_version = 6;
                """;
            command.ExecuteNonQuery();
        }

        var upgraded = Open();
        RecordTurn(upgraded, "turn-1", "tab-2", Log(Start.AddMinutes(1)));

        Assert.Equal([null, "turn-1"], upgraded.GetTurns(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).Select(t => t.Turn));
        Assert.Equal(2, upgraded.GetCalls(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).Count);
    }

    [Fact]
    public void A_turns_number_is_kept_and_a_history_from_before_numbers_gets_none()
    {
        var store = Open();
        store.AddTurns([new TurnRecord(Start, "tab-1", null, "opus", 1, 1, 1, 1, 0) { Turn = "turn-1" }]);
        store.Dispose();
        // Version 7 had calls and turn ids, but not the turns' numbers.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE turns DROP COLUMN turn_index; PRAGMA user_version = 7;";
            command.ExecuteNonQuery();
        }

        var upgraded = Open();
        upgraded.AddTurns([new TurnRecord(Start.AddMinutes(1), "tab-1", null, "opus", 1, 1, 1, 1, 0) { Turn = "turn-2", Index = 9 }]);

        Assert.Equal([null, 9], upgraded.GetTurns(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).Select(t => t.Index));
    }

    /// <summary>A turn of two calls on one model and a Read between them.</summary>
    private static TurnCallLog Log(DateTimeOffset at) => new(
        [
            new ModelCall(1, "msg_1", null, null, "opus", at, new CallTokens(3, 50, 10_000, 0, 200), 0, 0, CallKind.Call, 0.02),
            new ModelCall(2, "msg_2", null, null, "opus", at.AddSeconds(2), new CallTokens(2, 300, 10_200, 0, 400), 0, 0, CallKind.Call, 0.03),
        ],
        [new ToolStep(1, null, "Read", 900, false, at.AddSeconds(1))],
        [],
        null);

    private static void RecordTurn(UsageStore store, string turn, string tabId, TurnCallLog log)
    {
        var at = log.Calls[0].At;
        var spent = log.Calls.Aggregate(CallTokens.Zero, (sum, c) => sum.Plus(c.Tokens));
        store.AddTurns(
            [new TurnRecord(at, tabId, "session-1", "opus", spent.Input, spent.Output, spent.CacheWrite, spent.CacheRead, log.Calls.Sum(c => c.CostUsd)) { Turn = turn, IdleBefore = log.IdleBefore }],
            new TurnDetail(CallRecord.From(log, turn, tabId, "session-1"), StepRecord.From(log, turn), UsageEvent.From(log, turn, tabId, "session-1")));
    }

    private UsageStore Open()
    {
        var store = new UsageStore(DatabasePath, _time);
        _stores.Add(store);
        return store;
    }
}
