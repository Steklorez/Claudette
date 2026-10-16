using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Claudette.Usage.Tests;

/// <summary>Turns machines share through synced sessions (DESIGN.md §6, "Sharing across machines").</summary>
public sealed class SharedTurnsTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"claudette-shared-turns-{Guid.NewGuid():N}");
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

    // ---- The file -----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_session_usage_file_reads_back_as_it_was_written()
    {
        var usage = new SessionUsage(SessionUsageFile.FileVersion, Names(("desk", "Desktop")), Names(("tab-1", "Fix the loader")),
        [
            Turn("t1", Start, "desk", plan: "plan-a"),
            Turn("t2", Start.AddMinutes(1), "desk", account: "Studio", accountId: "acct-1") with { Project = "proj", ProjectName = "dethol" },
        ]);

        var read = SessionUsageFile.Read(SessionUsageFile.Write(usage))!;

        Assert.Equal(usage.Turns, read.Turns);
        Assert.Equal("Desktop", read.Machines["desk"]);
        Assert.Equal("Fix the loader", read.Tabs["tab-1"]);
    }

    [Fact]
    public void A_file_that_isnt_one_is_ignored_a_bad_turn_is_skipped_and_a_newer_format_has_no_turns()
    {
        Assert.Null(SessionUsageFile.Read("not json"));
        Assert.Null(SessionUsageFile.Read("""{"version":1}"""));

        var read = SessionUsageFile.Read("""
            {"version":1,"turns":[
              {"id":"ok","at":1790000000000,"machine":"desk","tab":"tab-1","model":"opus","input":5,"later":"ignored"},
              {"id":"no time","machine":"desk","tab":"tab-1","model":"opus"},
              {"at":1790000000000,"machine":"desk","tab":"tab-1","model":"opus"}]}
            """)!;
        Assert.Equal("ok", Assert.Single(read.Turns).Id);
        Assert.Equal(5, read.Turns[0].Input);

        var newer = SessionUsageFile.Read("""{"version":2,"turns":[{"id":"t","at":1790000000000,"machine":"m","tab":"t","model":"opus"}]}""")!;
        Assert.True(newer.IsNewerFormat);
        Assert.Empty(newer.Turns);
    }

    [Fact]
    public void Merging_keeps_both_sides_turns_once_and_the_newer_names()
    {
        var older = new SessionUsage(1, Names(("desk", "Old name")), Names(), [Turn("a", Start, "desk"), Turn("b", Start, "desk")]);
        var newer = new SessionUsage(1, Names(("desk", "Desktop"), ("lap", "Laptop")), Names(), [Turn("b", Start, "desk"), Turn("c", Start, "lap")]);

        var merged = SessionUsageFile.Merge(older, newer);

        Assert.Equal(["a", "b", "c"], merged.Turns.Select(t => t.Id).Order());
        Assert.Equal("Desktop", merged.Machines["desk"]);
        Assert.Equal("Laptop", merged.Machines["lap"]);
    }

    // ---- The store ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Another_machines_turns_are_added_once_with_their_machine_and_tab_names()
    {
        var store = Open();
        SharedTurn[] turns = [Turn("t1", Start, "lap", tab: "lap-tab"), Turn("t2", Start, "lap", tab: "lap-tab", model: "haiku")];

        var first = store.ImportTurns("s1", turns, Names(("lap", "Laptop")), Names(("lap-tab", "Refactor auth")), keepFor: null);
        var again = store.ImportTurns("s1", turns, Names(("lap", "Laptop")), Names(("lap-tab", "Refactor auth")), keepFor: null);

        Assert.Equal(2, first.Count);
        Assert.Empty(again);
        var tab = Assert.Single(store.GetTokensByTab(DateTimeOffset.MinValue));
        Assert.Equal(("lap-tab", "lap", "Refactor auth", 1), (tab.TabId, tab.Machine, tab.Name, tab.Turns));
        Assert.Equal("Laptop", store.GetMachineNames()["lap"]);
    }

    [Fact]
    public void Turns_older_than_the_history_keeps_or_the_last_clear_arent_added()
    {
        var store = Open();
        store.Clear();
        _time.Advance(TimeSpan.FromDays(2));

        var added = store.ImportTurns("s1",
        [
            Turn("before clear", Start.AddMinutes(-1), "lap"),
            Turn("too old", _time.GetUtcNow().AddDays(-1.5), "lap"),
            Turn("kept", _time.GetUtcNow().AddHours(-1), "lap"),
            // A clock ahead of this one's: kept as it is.
            Turn("from the future", _time.GetUtcNow().AddHours(3), "lap"),
        ], Names(), Names(), keepFor: TimeSpan.FromDays(1));

        Assert.Equal(["kept", "from the future"], added.Select(t => t.Id));
    }

    [Fact]
    public void A_sessions_usage_holds_this_machines_turns_and_the_ones_others_shared_but_not_turns_from_before_sharing()
    {
        var store = Open();
        store.AddTurns(
        [
            new TurnRecord(Start, "my-tab", "s1", "opus", 100, 10, 0, 0, 0.25, "/work/dethol") { ProjectKey = "dethol-key", ProjectName = "dethol", Plan = "plan-a" },
            new TurnRecord(Start, "my-tab", "s2", "opus", 1, 1, 0, 0, 0.01),
        ]);
        store.SetTabName("my-tab", "Fix the loader");
        store.ImportTurns("s1", [Turn("theirs", Start.AddMinutes(5), "lap", tab: "lap-tab")], Names(("lap", "Laptop")), Names(("lap-tab", "Review")), keepFor: null);
        // A turn recorded before turns were shared has no id.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO turns (timestamp, tab_id, session_id, model, input, output, cache_write, cache_read, cost_usd) VALUES (0, 'old', 's1', 'opus', 1, 1, 1, 1, 9)";
            command.ExecuteNonQuery();
        }

        var usage = store.GetSessionUsage("s1", "desk", "Desktop");

        Assert.Equal([("desk", "my-tab", "dethol-key", "plan-a"), ("lap", "lap-tab", null, null)], usage.Turns.Select(t => (t.Machine, t.Tab, t.Project, t.Plan)));
        Assert.Equal(("Desktop", "Laptop"), (usage.Machines["desk"], usage.Machines["lap"]));
        Assert.Equal(("Fix the loader", "Review"), (usage.Tabs["my-tab"], usage.Tabs["lap-tab"]));
    }

    [Fact]
    public void A_projects_turns_on_every_machine_and_in_every_folder_of_it_are_one_project()
    {
        var store = Open();
        store.AddTurns(
        [
            // Before keys: backfilled from its folder.
            new TurnRecord(Start, "tab-1", "s1", "opus", 100, 0, 0, 0, 1.0, "/work/dethol"),
            // A worktree of it, with the key.
            new TurnRecord(Start, "tab-2", "s2", "opus", 50, 0, 0, 0, 0.5, "/work/dethol/.claude/worktrees/fix") { ProjectKey = "dethol-key", ProjectName = "dethol" },
            new TurnRecord(Start, "tab-3", "s3", "opus", 10, 0, 0, 0, 0.1, "/work/other") { ProjectKey = "other-key", ProjectName = "other" },
        ]);
        store.ImportTurns("s9", [Turn("lap-1", Start, "lap") with { Project = "dethol-key", ProjectName = "dethol", Input = 400, CostUsd = 4.0 }], Names(("lap", "Laptop")), Names(), keepFor: null);

        Assert.Equal(1, store.BackfillProjectKeys(folder => folder == "/work/dethol" ? ("dethol-key", "dethol") : null));

        var sums = store.GetTokensByProject(DateTimeOffset.MinValue);
        Assert.Equal(
            [("dethol-key", (string?)"lap", 410L), ("dethol-key", null, 150L), ("other-key", null, 10L)],
            sums.Select(s => (s.Key, s.Machine, s.Total)));
        Assert.Null(sums[0].Project);
        Assert.NotNull(sums[1].Project);
    }

    [Fact]
    public void An_api_key_accounts_spend_counts_other_machines_turns_under_its_name_and_the_plans_tabs_leave_them_out()
    {
        var store = Open();
        store.AddTurns([new TurnRecord(Start, "tab-1", "s1", "opus", 100, 0, 0, 0, 0.25, Account: "local-studio") { AccountName = "Studio" }]);
        store.ImportTurns("s2",
        [
            Turn("same name", Start, "lap", account: " studio ", accountId: "lap-studio") with { CostUsd = 1.0 },
            Turn("other name", Start, "lap", account: "Home", accountId: "lap-home") with { CostUsd = 5.0 },
            Turn("plan", Start, "lap", plan: "plan-a") with { CostUsd = 7.0 },
        ], Names(("lap", "Laptop")), Names(), keepFor: null);

        Assert.Equal(1.25, store.GetSpend("local-studio", Start, "Studio"), precision: 6);
        Assert.Equal(0.25, store.GetSpend("local-studio", Start), precision: 6);
        Assert.Equal(["tab-1"], store.GetTokensByTab(Start).Where(t => t.Machine is null).Select(t => t.TabId));
        Assert.Equal(["lap"], store.GetTokensByTab(Start, planOnly: true).Select(t => t.Machine));
        Assert.Equal(
            [(null, null, "lap"), ("lap-home", "Home", "lap"), ("lap-studio", "studio", "lap"), ("local-studio", null, null)],
            store.GetTokensByAccount(Start).Select(a => (a.Account, a.AccountName, a.Machine)).OrderBy(a => a.Account, StringComparer.Ordinal));
    }

    [Fact]
    public void A_history_from_before_sharing_is_upgraded_and_its_turns_are_never_shared()
    {
        var store = Open();
        store.AddTurns([new TurnRecord(Start, "tab-1", "s1", "opus", 1, 1, 1, 1, 0.5, "/work/api", "work")]);
        store.Dispose();
        _stores.Remove(store);
        // Version 5 had none of the sharing columns.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE old_turns AS SELECT id, timestamp, tab_id, session_id, model, input, output, cache_write, cache_read, cost_usd, project, account FROM turns;
                DROP TABLE turns; ALTER TABLE old_turns RENAME TO turns; DROP TABLE machines; PRAGMA user_version = 5;
                """;
            command.ExecuteNonQuery();
        }

        var upgraded = Open();
        upgraded.AddTurns([new TurnRecord(Start.AddMinutes(1), "tab-1", "s1", "opus", 2, 0, 0, 0, 0.25)]);

        Assert.Equal(0.75, upgraded.GetSpend(null, DateTimeOffset.MinValue) + upgraded.GetSpend("work", DateTimeOffset.MinValue), precision: 6);
        Assert.Equal(2, Assert.Single(upgraded.GetSessionUsage("s1", "desk", "Desktop").Turns).Input);
    }

    [Fact]
    public void Clearing_the_history_forgets_other_machines_and_pruning_those_without_turns()
    {
        var store = Open();
        store.ImportTurns("s1", [Turn("t", Start, "lap")], Names(("lap", "Laptop")), Names(), keepFor: null);

        _time.Advance(TimeSpan.FromDays(10));
        store.Prune(TimeSpan.FromDays(1));
        Assert.Empty(store.GetMachineNames());

        store.ImportTurns("s1", [Turn("u", _time.GetUtcNow(), "lap")], Names(("lap", "Laptop")), Names(), keepFor: null);
        store.Clear();
        Assert.Empty(store.GetMachineNames());
        Assert.Empty(store.GetTokensByTab(DateTimeOffset.MinValue));
    }

    // ---- Clear on every machine ---------------------------------------------------------------------------------------

    [Fact]
    public void The_note_clearing_every_machine_reads_back_and_one_without_a_time_isnt_one()
    {
        var note = new UsageCleared(Start, "Laptop", ResetTabTotals: true);

        Assert.Equal(note, UsageSharing.ReadCleared(UsageSharing.WriteCleared(note)));
        Assert.Null(UsageSharing.ReadCleared("""{"version":1,"machine":"Laptop"}"""));
        Assert.Null(UsageSharing.ReadCleared("not json"));
    }

    [Fact]
    public void Clearing_says_when_and_nothing_from_before_then_is_imported()
    {
        var store = Open();
        Assert.Null(store.ClearedAt);

        var at = store.Clear();

        Assert.Equal(at, store.ClearedAt);
        Assert.Empty(store.ImportTurns("s1", [Turn("old", Start.AddMinutes(-1), "lap")], Names(), Names(), keepFor: null));
    }

    [Fact]
    public void Another_machines_clear_deletes_what_came_before_it_and_keeps_what_came_after()
    {
        var store = Open();
        store.AddTurns([new TurnRecord(Start.AddMinutes(-10), "tab-1", "s1", "opus", 1, 0, 0, 0, 0.1), new TurnRecord(Start.AddMinutes(10), "tab-2", "s2", "opus", 2, 0, 0, 0, 0.2)]);
        store.SetTabName("tab-1", "Before");
        store.SetTabName("tab-2", "After");
        store.ImportTurns("s3", [Turn("theirs", Start.AddMinutes(-5), "lap", tab: "lap-tab")], Names(("lap", "Laptop")), Names(("lap-tab", "Review")), keepFor: null);

        Assert.Equal(2, store.ClearThrough(Start));

        Assert.Equal([("tab-2", "After")], store.GetTokensByTab(DateTimeOffset.MinValue).Select(t => (t.TabId, t.Name)));
        Assert.Empty(store.GetMachineNames());
        Assert.Equal(Start, store.ClearedAt);
        Assert.Empty(store.ImportTurns("s3", [Turn("also old", Start.AddMinutes(-1), "lap")], Names(), Names(), keepFor: null));
        // A clear this machine made later stands.
        var later = store.Clear();
        store.ClearThrough(Start);
        Assert.Equal(later, store.ClearedAt);
    }

    private UsageStore Open()
    {
        var store = new UsageStore(DatabasePath, _time);
        _stores.Add(store);
        return store;
    }

    private static SharedTurn Turn(string id, DateTimeOffset at, string machine, string tab = "tab-x", string model = "opus", string? plan = null, string? account = null,
        string? accountId = null) =>
        new(id, at, machine, tab, model, 100, 10, 0, 0, 0.01, Plan: plan, AccountId: accountId, Account: account);

    private static Dictionary<string, string> Names(params (string Id, string Name)[] names) => names.ToDictionary(n => n.Id, n => n.Name);
}
