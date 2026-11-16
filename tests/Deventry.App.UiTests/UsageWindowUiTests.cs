using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Sessions;
using Deventry.Core.Settings;
using Deventry.Usage;

namespace Deventry.App.UiTests;

/// <summary>The Usage panel's Projects list (DESIGN.md §6, "Usage history").</summary>
public class UsageWindowUiTests
{
    [AvaloniaFact]
    public async Task The_projects_list_shows_each_folder_and_follows_the_period()
    {
        await using var h = new TabTestHarness();
        await using var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        using var header = new UsageViewModel(h.Services, tracker);
        var now = h.Time.GetUtcNow();
        tracker.Store.AddTurns(
        [
            new TurnRecord(now.AddDays(-20), "tab-1", null, "fable", 5_000, 0, 0, 0, 2, "/work/old"),
            new TurnRecord(now.AddHours(-1), "tab-2", null, "fable", 3_000, 0, 0, 0, 1, "/work/api"),
        ]);
        var panel = new UsagePanelViewModel(h.Services, tracker, header, _ => null);
        await panel.RefreshAsync();
        var window = new Views.UsageWindow { DataContext = panel, Width = 900, Height = 900 };
        window.Show();
        UiText.Settle(window);

        Assert.Contains("api", Texts(window));
        Assert.DoesNotContain("old", Texts(window));
        var period = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.ItemsSource == panel.Periods);
        Assert.Equal("This session window", period.SelectedItem?.ToString());

        period.SelectedItem = panel.Periods.Single(p => p.Period == UsagePanelPeriod.Month);
        await panel.RefreshAsync();
        UiText.Settle(window);

        Assert.Contains("old", Texts(window));
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_token_sections_show_where_the_tokens_went_and_each_tab_model_and_tool()
    {
        await using var h = new TabTestHarness();
        await using var tracker = new UsageTracker(h.Services, new UsageStore(Path.Combine(h.Root, "usage.db"), h.Time));
        using var header = new UsageViewModel(h.Services, tracker);
        var now = h.Time.GetUtcNow();
        const string opus = "claude-opus-5-5";
        CallRecord[] calls =
        [
            new("t1", "tab-1", "s1", now.AddHours(-1), 1, "m1", null, null, opus, 3, 40, 0, 0, 20_300, 0, 0, CallKind.CacheExpired, 0.21),
            new("t1", "tab-1", "s1", now.AddHours(-1).AddSeconds(4), 2, "m2", null, null, opus, 2, 120, 20_300, 0, 240, 0, 0, CallKind.Call, 0.02),
        ];
        tracker.Store.AddTurns(
            [new TurnRecord(now.AddHours(-1).AddSeconds(5), "tab-1", "s1", opus, 5, 160, 20_540, 20_300, 0.23, "/work/starfall") { Turn = "t1" }],
            new TurnDetail(calls, [new StepRecord("t1", 1, null, "Read", 200, false, now.AddHours(-1).AddSeconds(2))], []));
        tracker.Store.SetTabName("tab-1", "Grappling hook");
        var panel = new UsagePanelViewModel(h.Services, tracker, header, _ => null);
        await panel.RefreshAsync();
        var window = new Views.UsageWindow { DataContext = panel, Width = 900, Height = 1600 };
        window.Show();
        UiText.Settle(window);

        var texts = Texts(window);
        Assert.Contains("Tokens over", texts);
        Assert.Contains("Where the tokens went", texts);
        Assert.Contains("1 turn began after the cache expired: $0.21 to read its conversation again.", texts);
        Assert.Contains("Grappling hook", texts);
        Assert.Contains("starfall · closed", texts);
        Assert.Contains("1 expired cache", texts);
        Assert.Contains("Opus 5.5", texts);
        Assert.Contains("Read", texts);
        Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("openrow"));
        window.Close();
    }

    private static List<string?> Texts(Window window) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text)];
}
