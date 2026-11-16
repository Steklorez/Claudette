using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.Core.Sessions;
using Deventry.Usage;

namespace Deventry.App.UiTests;

/// <summary>The Tokens tool window's page (DESIGN.md §18, "The Tokens tool window").</summary>
public class TokensViewUiTests
{
    private const string Opus = "claude-opus-5-5";

    [AvaloniaFact]
    public async Task The_page_shows_the_tab_its_latest_turns_and_findings_and_a_turns_row_opens_the_breakdown_on_it()
    {
        await using var h = new TabTestHarness();
        await using var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        var tab = await h.OpenTabAsync();
        var now = h.Time.GetUtcNow();
        Record(tracker.Store, tab.Id, "t1", 1, now.AddMinutes(-90), CallKind.Call);
        Record(tracker.Store, tab.Id, "t2", 2, now.AddMinutes(-5), CallKind.CacheExpired);
        tab.Tokens.SetWanted("window", true);
        await TabTestHarness.Eventually(() => tab.Tokens.LatestTurns.Count == 2, "the turns");
        string? asked = null;
        h.Shell.TokenBreakdownRequested += (_, id, _) => asked = id;
        var window = new Window { Content = new Views.TokensView { DataContext = tab.Tokens }, Width = 340, Height = 1200 };
        window.Show();
        UiText.Settle(window);

        var texts = Texts(window);
        Assert.Contains("This tab", texts);
        Assert.Contains("Latest turns", texts);
        Assert.Contains("Findings", texts);
        Assert.Contains("All 2 turns in the breakdown ›", texts);
        Assert.Contains(texts, t => t?.StartsWith("Turn 2 was sent 1h 25m after turn 1", StringComparison.Ordinal) == true);
        // Newest first.
        var rows = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("latestturn")).ToList();
        Assert.Equal(2, rows.Count);

        rows[0].Command!.Execute(rows[0].CommandParameter);

        Assert.Equal("t2", asked);
        window.Close();
    }

    /// <summary>A turn of two calls on Opus with a Read between them, its first of the kind given.</summary>
    private static void Record(UsageStore store, string tabId, string turn, int index, DateTimeOffset at, CallKind first)
    {
        var expired = first == CallKind.CacheExpired;
        CallRecord[] calls =
        [
            new(turn, tabId, "s1", at, 1, $"{turn}_1", null, null, Opus, 3, 40, expired ? 0 : 20_000, 0, expired ? 20_300 : 300, 0, 0, first, expired ? 0.21 : 0.02),
            new(turn, tabId, "s1", at.AddSeconds(4), 2, $"{turn}_2", null, null, Opus, 2, 120, 20_300, 0, 240, 0, 0, CallKind.Call, 0.02),
        ];
        store.AddTurns(
            [new TurnRecord(at.AddSeconds(5), tabId, "s1", Opus, 5, 160, calls.Sum(c => c.CacheWrite), calls.Sum(c => c.CacheRead), calls.Sum(c => c.CostUsd))
            {
                Turn = turn,
                Index = index,
                IdleBefore = expired ? TimeSpan.FromMinutes(85) : null,
            }],
            new TurnDetail(calls, [new StepRecord(turn, 1, null, "Read", 200, false, at.AddSeconds(2))], []));
    }

    private static List<string?> Texts(Window window) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? t.Inlines?.Text)];
}
