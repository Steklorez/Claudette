using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Sessions;
using Deventry.Usage;

namespace Deventry.App.UiTests;

/// <summary>A tab's token breakdown in its window (DESIGN.md §18, "The window").</summary>
public class TokenBreakdownWindowUiTests
{
    private const string Opus = "claude-opus-5-5";

    [AvaloniaFact]
    public async Task The_window_shows_the_tabs_turns_and_a_click_opens_a_turns_calls()
    {
        await using var h = new TabTestHarness();
        await using var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        var tab = await h.OpenTabAsync();
        var now = h.Time.GetUtcNow();
        Record(tracker.Store, tab.Id, "t1", now.AddMinutes(-90), CallKind.Call);
        Record(tracker.Store, tab.Id, "t2", now.AddMinutes(-5), CallKind.CacheExpired);
        using var breakdown = new TokenBreakdownViewModel(h.Services, tab);
        breakdown.Refresh();
        await TabTestHarness.Eventually(() => breakdown.Turns.Count == 2, "the turns");
        var window = new Views.TokenBreakdownWindow { DataContext = breakdown, Width = 1240, Height = 1400 };
        window.Show();
        UiText.Settle(window);

        var texts = Texts(window);
        Assert.Contains($"Token breakdown · {tab.DisplayName}", texts);
        Assert.Contains("What the tokens were", texts);
        Assert.Contains("Turn by turn", texts);
        Assert.Contains("Turn 2", texts);
        Assert.Contains("Turn 2 was sent 1h 25m after turn 1, after the 1-hour prompt cache had expired. Its first call wrote the whole conversation to the cache again: "
            + "20.3k tokens, $0.21, 78% of this tab's cost.", texts);
        Assert.DoesNotContain(texts, t => t?.Contains("Claude's answer", StringComparison.Ordinal) == true);

        var row = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("turnrow"));
        row.Command!.Execute(null);
        UiText.Settle(window);

        Assert.Contains(Texts(window), t => t?.Contains("c2 Claude's answer", StringComparison.Ordinal) == true);
        window.Close();
    }

    [AvaloniaFact]
    public async Task In_a_narrow_window_the_findings_and_tables_go_under_the_turns()
    {
        await using var h = new TabTestHarness();
        await using var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        h.Services.UseUsageTracker(tracker);
        var tab = await h.OpenTabAsync();
        Record(tracker.Store, tab.Id, "t1", h.Time.GetUtcNow().AddMinutes(-5), CallKind.Call);
        using var breakdown = new TokenBreakdownViewModel(h.Services, tab);
        breakdown.Refresh();
        await TabTestHarness.Eventually(() => breakdown.Turns.Count == 1, "the turn");
        var window = new Views.TokenBreakdownWindow { DataContext = breakdown, Width = 1240, Height = 900 };
        window.Show();
        UiText.Settle(window);
        var right = window.FindControl<StackPanel>("RightColumn")!;
        Assert.Equal((0, 2), (Grid.GetRow(right), Grid.GetColumn(right)));

        window.Width = 900;
        UiText.Settle(window);

        Assert.Equal((1, 0), (Grid.GetRow(right), Grid.GetColumn(right)));
        window.Close();
    }

    /// <summary>A turn of two calls on Opus with a Read between them, its first of the kind given.</summary>
    private static void Record(UsageStore store, string tabId, string turn, DateTimeOffset at, CallKind first)
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
                IdleBefore = expired ? TimeSpan.FromMinutes(85) : null,
            }],
            new TurnDetail(calls, [new StepRecord(turn, 1, null, "Read", 200, false, at.AddSeconds(2))], []));
    }

    private static List<string?> Texts(Window window) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? t.Inlines?.Text)];
}
