using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Sessions;
using Deventry.Usage;

namespace Deventry.App.Tests;

/// <summary>
/// The Usage panel's token sections (DESIGN.md §18, "The Usage panel"): where the tokens went over the period, every tab's
/// tokens with the way to its breakdown, closed tabs' too, and the models and tools.
/// </summary>
public class UsagePanelTokensTests
{
    private const string Opus = "claude-opus-5-5";

    [Fact]
    public async Task The_period_says_where_the_tokens_went_and_each_tab_model_and_tool()
    {
        await using var h = new TabTestHarness();
        await using var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        using var header = new UsageViewModel(h.Services, tracker);
        var tab = await h.OpenTabAsync();
        var now = h.Time.GetUtcNow();
        Record(tracker.Store, tab.Id, "t1", now.AddHours(-2), expired: true, "/work/starfall");
        Record(tracker.Store, "tab-closed", "t2", now.AddHours(-1), expired: false, "/work/api", account: "acct-studio", accountName: "Studio");
        tracker.Store.SetTabName("tab-closed", "refactor auth");
        var panel = new UsagePanelViewModel(h.Services, tracker, header, id => h.Shell.AllTabs.FirstOrDefault(t => t.Id == id)?.DisplayName);

        await panel.RefreshAsync();

        Assert.True(panel.HasTurns);
        Assert.Equal(["Cache read", "Cache write", "Fresh input", "Output"], panel.Kinds.Select(k => k.Name));
        Assert.EndsWith("about $0.27 · 2 turns in 2 tabs", panel.TotalsText, StringComparison.Ordinal);
        Assert.Equal("1 turn began after the cache expired: $0.21 to read its conversation again.", panel.ExpiredText);
        // The costliest first: the open tab by its name, the closed one by the name it last had, with its account.
        Assert.Equal([(tab.DisplayName, "starfall", "$0.23", "1 expired cache"), ("refactor auth", "api · closed · Studio", "$0.04", (string?)null)],
            panel.Tabs.Select(t => (t.Name, t.Detail, t.Cost, t.Expired)));
        Assert.Equal(1, panel.Tabs[0].Scale);
        var model = Assert.Single(panel.Models);
        Assert.Equal(("Opus 5.5", "4", "$0.27"), (model.Name, model.Calls, model.Cost));
        var tool = Assert.Single(panel.Tools);
        Assert.Equal(("Read", "2"), (tool.Tool, tool.Calls));
    }

    [Fact]
    public async Task A_tabs_row_opens_its_breakdown_the_open_tabs_own_or_from_the_history()
    {
        await using var h = new TabTestHarness();
        await using var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        using var header = new UsageViewModel(h.Services, tracker);
        var tab = await h.OpenTabAsync();
        var now = h.Time.GetUtcNow();
        Record(tracker.Store, tab.Id, "t1", now.AddHours(-2), expired: true, "/work/starfall");
        Record(tracker.Store, "tab-closed", "t2", now.AddHours(-1), expired: false, "/work/api", account: "acct-studio", accountName: "Studio");
        tracker.Store.SetTabName("tab-closed", "refactor auth");
        var panel = new UsagePanelViewModel(h.Services, tracker, header, id => h.Shell.AllTabs.FirstOrDefault(t => t.Id == id)?.DisplayName, h.Shell.ShowTokenBreakdown);
        await panel.RefreshAsync();
        var asked = new List<ITokenBreakdownSubject>();
        h.Shell.TokenBreakdownRequested += (subject, _, _) => asked.Add(subject);

        panel.OpenTabCommand.Execute(panel.Tabs[0]);
        panel.OpenTabCommand.Execute(panel.Tabs[1]);

        Assert.Same(tab, Assert.IsType<OpenTabBreakdown>(asked[0]).Tab);
        var closed = Assert.IsType<HistoryTabBreakdown>(asked[1]);
        Assert.Equal(("tab-closed", "refactor auth", "api", false), (closed.Id, closed.Name, closed.Project, closed.UsesPlan));

        // Its window reads its turns from the history, without its conversation: they go by their numbers.
        using var breakdown = new TokenBreakdownViewModel(h.Services, closed);
        breakdown.Refresh();
        await TabTestHarness.Eventually(() => breakdown.Turns.Count == 1, "the closed tab's turn");
        Assert.Equal("Token breakdown · refactor auth", breakdown.Title);
        Assert.Equal("Turn 1", breakdown.Turns[0].Prompt);
        Assert.StartsWith("api · 1 turn since", breakdown.Line, StringComparison.Ordinal);
        Assert.Equal("its account doesn't use the plan", breakdown.Tiles.Single(t => t.Label == "Session limit").Detail);
    }

    [Fact]
    public async Task With_no_turns_in_the_period_the_sections_say_so()
    {
        await using var h = new TabTestHarness();
        await using var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        using var header = new UsageViewModel(h.Services, tracker);
        Record(tracker.Store, "tab-old", "t1", h.Time.GetUtcNow().AddDays(-3), expired: false, "/work/api");
        var panel = new UsagePanelViewModel(h.Services, tracker, header, _ => null);

        await panel.RefreshAsync();

        Assert.False(panel.HasTurns);
        Assert.False(panel.HasTabs);
        Assert.Null(panel.ExpiredText);

        panel.SelectedPeriod = panel.Periods.Single(p => p.Period == Core.Settings.UsagePanelPeriod.Week);
        await panel.RefreshAsync();

        Assert.True(panel.HasTurns);
        Assert.Equal(["A closed tab"], panel.Tabs.Select(t => t.Name));
    }

    /// <summary>A turn of two calls on Opus with a Read between them, its first having found the cache expired or not.</summary>
    private static void Record(UsageStore store, string tabId, string turn, DateTimeOffset at, bool expired, string project, string? account = null, string? accountName = null)
    {
        CallRecord[] calls =
        [
            new(turn, tabId, "s1", at, 1, $"{turn}_1", null, null, Opus, 3, 40, expired ? 0 : 20_000, 0, expired ? 20_300 : 300, 0, 0,
                expired ? CallKind.CacheExpired : CallKind.Call, expired ? 0.21 : 0.02),
            new(turn, tabId, "s1", at.AddSeconds(4), 2, $"{turn}_2", null, null, Opus, 2, 120, 20_300, 0, 240, 0, 0, CallKind.Call, 0.02),
        ];
        store.AddTurns(
            [new TurnRecord(at.AddSeconds(5), tabId, "s1", Opus, 5, 160, calls.Sum(c => c.CacheWrite), calls.Sum(c => c.CacheRead), calls.Sum(c => c.CostUsd), project, account)
            {
                Turn = turn,
                AccountName = accountName,
            }],
            new TurnDetail(calls, [new StepRecord(turn, 1, null, "Read", 200, false, at.AddSeconds(2))], []));
    }
}
