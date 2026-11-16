using Deventry.Core.Sessions;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Usage.Tests;

/// <summary>
/// Calls, tools' results and events machines share with a synced session's turns (DESIGN.md §18, "What's recorded"; §6,
/// "Sharing across machines").
/// </summary>
public sealed class SharedCallsTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"deventry-shared-calls-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _time = new(Start.AddHours(1));
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

    // ---- The file -----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_session_usage_file_carries_its_turns_calls_steps_and_events()
    {
        var usage = Usage("lap", "turn-1", Start);

        var read = SessionUsageFile.Read(SessionUsageFile.Write(usage))!;

        Assert.Equal(("turn-1", (int?)4), (Assert.Single(read.Turns).Turn, read.Turns[0].Index));
        Assert.Equal(usage.Calls, read.Calls);
        Assert.Equal(usage.Steps, read.Steps);
        Assert.Equal(usage.Events, read.Events);
    }

    [Fact]
    public void A_file_from_before_calls_has_none_and_a_bad_entry_is_skipped()
    {
        Assert.Empty(SessionUsageFile.Read("""{"version":1,"turns":[]}""")!.Calls);

        var read = SessionUsageFile.Read("""
            {"version":1,"turns":[],
             "calls":[
               {"turn":"t","seq":1,"at":1790000000000,"machine":"lap","tab":"tab","model":"opus","input":5,"kind":99,"later":"ignored"},
               {"turn":"t","seq":2,"machine":"lap","tab":"tab","model":"opus"}],
             "steps":[{"turn":"t","call":1,"tool":"Read","tokens":40,"at":1790000000000,"machine":"lap"},{"turn":"t","tool":"Read"}],
             "events":[{"tab":"tab","at":1790000000000,"kind":0,"machine":"lap"},{"tab":"tab","at":1790000000000,"kind":42,"machine":"lap"}]}
            """)!;

        // A kind this Deventry doesn't know reads as a call; an event of one is left out.
        var call = Assert.Single(read.Calls);
        Assert.Equal((5L, CallKind.Call, "lap"), (call.Input, call.Kind, call.Machine));
        Assert.Equal(40, Assert.Single(read.Steps).Tokens);
        Assert.Equal(UsageEventKind.Compacted, Assert.Single(read.Events).Kind);
    }

    [Fact]
    public void Merging_keeps_each_turns_calls_once_and_its_steps_from_one_file()
    {
        var older = Usage("desk", "turn-1", Start);
        var newer = Usage("desk", "turn-1", Start) with { Turns = [] };
        newer = SessionUsageFile.Merge(newer, Usage("lap", "turn-2", Start.AddMinutes(5)));

        var merged = SessionUsageFile.Merge(older, newer);

        Assert.Equal([("turn-1", 1), ("turn-1", 2), ("turn-2", 1), ("turn-2", 2)], merged.Calls.Select(c => (c.Turn, c.Seq)).Order());
        Assert.Equal(["turn-1", "turn-2"], merged.Steps.Select(s => s.Turn).Order());
        Assert.Equal(2, merged.Events.Count);
    }

    // ---- The store ----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_sessions_usage_holds_this_machines_calls_under_its_id()
    {
        var store = Open();
        var log = Log(Start);
        store.AddTurns([new TurnRecord(Start, "my-tab", "s1", "opus", 5, 350, 600, 20_200, 0.05) { Turn = "turn-1", Index = 7 }],
            new TurnDetail(CallRecord.From(log, "turn-1", "my-tab", "s1"), StepRecord.From(log, "turn-1"),
                [new UsageEvent("my-tab", "s1", "turn-1", Start.AddSeconds(3), UsageEventKind.Compacted, 150_000, "auto")]));
        // Another session's.
        store.AddTurns([new TurnRecord(Start, "my-tab", "s2", "opus", 5, 350, 600, 20_200, 0.05) { Turn = "turn-9" }],
            new TurnDetail(CallRecord.From(log, "turn-9", "my-tab", "s2"), [], []));

        var usage = store.GetSessionUsage("s1", "desk", "Desktop");

        Assert.Equal(("turn-1", (int?)7), (Assert.Single(usage.Turns).Turn, usage.Turns[0].Index));
        Assert.Equal([("turn-1", "desk"), ("turn-1", "desk")], usage.Calls.Select(c => (c.Turn, c.Machine)));
        Assert.All(usage.Calls, c => Assert.Null(c.SessionId));
        Assert.Equal("desk", Assert.Single(usage.Steps).Machine);
        Assert.Equal(("desk", 150_000L), (Assert.Single(usage.Events).Machine, usage.Events[0].Value));
    }

    [Fact]
    public void Another_machines_calls_are_added_once_and_only_for_the_turns_this_machine_counts()
    {
        var store = Open();
        var usage = SessionUsageFile.Merge(Usage("lap", "theirs", Start), Usage("lap", "other plan", Start.AddMinutes(2)));
        store.ImportTurns("s1", [usage.Turns[0]], usage.Machines, usage.Tabs, keepFor: null);

        Assert.Equal(2, store.ImportDetail("s1", usage, new HashSet<string> { "theirs" }, keepFor: null));
        Assert.Equal(0, store.ImportDetail("s1", usage, new HashSet<string> { "theirs" }, keepFor: null));

        var calls = store.GetCalls(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        Assert.Equal([("theirs", "lap", "s1"), ("theirs", "lap", "s1")], calls.Select(c => (c.Turn, c.Machine, c.SessionId)));
        // The turn keeps its number in the tab that ran it.
        Assert.Equal(4, Assert.Single(store.GetTurns(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).Index);
        Assert.Single(store.GetSteps(["theirs"]));
        Assert.Empty(store.GetSteps(["other plan"]));
        Assert.Equal("s1", Assert.Single(store.GetEvents(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).SessionId);
        // And they go back out with the session's usage, under the machine that ran them.
        Assert.All(store.GetSessionUsage("s1", "desk", "Desktop").Calls, c => Assert.Equal("lap", c.Machine));
    }

    [Fact]
    public void Calls_older_than_the_history_keeps_or_the_last_clear_arent_added()
    {
        var store = Open();
        store.Clear();
        _time.Advance(TimeSpan.FromDays(2));
        var usage = SessionUsageFile.Merge(Usage("lap", "before clear", Start.AddMinutes(30)), Usage("lap", "kept", _time.GetUtcNow().AddHours(-1)));

        store.ImportDetail("s1", usage, new HashSet<string> { "before clear", "kept" }, keepFor: TimeSpan.FromDays(1));

        Assert.All(store.GetCalls(DateTimeOffset.MinValue, DateTimeOffset.MaxValue), c => Assert.Equal("kept", c.Turn));
    }

    /// <summary>A turn of two calls on one model, a Read between them and a compaction, as <paramref name="machine"/> shares it.</summary>
    private static SessionUsage Usage(string machine, string turn, DateTimeOffset at)
    {
        var log = Log(at);
        return new SessionUsage(SessionUsageFile.FileVersion, new Dictionary<string, string> { [machine] = machine }, new Dictionary<string, string> { ["tab"] = "Fix it" },
            [new SharedTurn($"{turn}-opus", at, machine, "tab", "opus", 5, 350, 600, 20_200, 0.05) { Turn = turn, Index = 4 }])
        {
            Calls = [.. CallRecord.From(log, turn, "tab", null).Select(c => c with { Machine = machine })],
            Steps = [.. StepRecord.From(log, turn).Select(s => s with { Machine = machine })],
            Events = [new UsageEvent("tab", null, turn, at.AddSeconds(3), UsageEventKind.Compacted, 150_000, "auto") { Machine = machine }],
        };
    }

    private static TurnCallLog Log(DateTimeOffset at) => new(
        [
            new ModelCall(1, "msg_1", null, null, "opus", at, new CallTokens(3, 50, 10_000, 0, 200), 0, 0, CallKind.Call, 0.02),
            new ModelCall(2, "msg_2", "toolu_1", "Explore", "opus", at.AddSeconds(2), new CallTokens(2, 300, 10_200, 400, 0), 1, 0, CallKind.Call, 0.03),
        ],
        [new ToolStep(1, null, "Read", 900, false, at.AddSeconds(1)) { ToolUseId = "toolu_read" }],
        [],
        null);

    private UsageStore Open()
    {
        var store = new UsageStore(Path.Combine(_root, "usage.db"), _time);
        _stores.Add(store);
        return store;
    }
}
