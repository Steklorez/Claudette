using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;

namespace Claudette.App.Tests;

/// <summary>Tabs side by side (DESIGN.md §18, "Split panes").</summary>
public class SplitPanesTests
{
    private static async Task<(TabTestHarness H, TabViewModel A, TabViewModel B, TabViewModel C)> ThreeTabsAsync()
    {
        var h = new TabTestHarness();
        h.Factory.ProcessPerSession = true;
        var a = await ThreadScript.OpenAsync(h, "Alpha");
        var b = await ThreadScript.OpenAsync(h, "Beta");
        var c = await ThreadScript.OpenAsync(h, "Gamma");
        h.Shell.SelectedTab = a;
        return (h, a, b, c);
    }

    [Fact]
    public async Task One_tab_on_its_own_is_on_screen_and_the_others_arent()
    {
        var (h, a, b, c) = await ThreeTabsAsync();
        await using var _ = h;

        Assert.Equal([a], h.Shell.Panes);
        Assert.False(h.Shell.IsSplit);
        Assert.True(a is { IsShown: true, IsSelected: true, IsInSplit: false, PaneIndex: 0 });
        Assert.True(b is { IsShown: false, PaneIndex: -1 });
        Assert.False(c.IsShown);
    }

    [Fact]
    public async Task Open_beside_puts_a_tab_after_the_focused_one_and_focuses_it()
    {
        var (h, a, b, c) = await ThreeTabsAsync();
        await using var _ = h;

        h.Shell.OpenBesideCommand.Execute(c);

        Assert.Equal([a, c], h.Shell.Panes);
        Assert.True(h.Shell.IsSplit);
        Assert.Same(c, h.Shell.SelectedTab);
        Assert.True(a is { IsShown: true, IsSelected: false, IsInSplit: true, PaneIndex: 0 });
        Assert.True(c is { IsShown: true, IsSelected: true, IsInSplit: true, PaneIndex: 1 });
        Assert.False(b.IsShown);
        Assert.Equal([a.Id, c.Id], h.Services.State.PaneTabIds);

        // Beside the focused one, so between them.
        h.Shell.SelectedTab = a;
        h.Shell.OpenBesideCommand.Execute(b);
        Assert.Equal([a, b, c], h.Shell.Panes);
    }

    [Fact]
    public async Task Picking_a_tab_on_screen_focuses_it_and_any_other_takes_the_focused_panes_place()
    {
        var (h, a, b, c) = await ThreeTabsAsync();
        await using var _ = h;
        h.Shell.OpenBesideCommand.Execute(b);

        // A click in a pane, or its row in the sidebar.
        a.FocusPane();
        Assert.Same(a, h.Shell.SelectedTab);
        Assert.Equal([a, b], h.Shell.Panes);

        h.Shell.SelectedTab = c;

        Assert.Equal([c, b], h.Shell.Panes);
        Assert.False(a.IsShown);
        Assert.True(b is { IsShown: true, IsSelected: false });
    }

    [Fact]
    public async Task With_every_pane_taken_a_tab_opened_beside_replaces_the_last_one_not_focused()
    {
        var (h, a, b, c) = await ThreeTabsAsync();
        await using var _ = h;
        var d = await ThreadScript.OpenAsync(h, "Delta");
        h.Shell.SelectedTab = a;
        h.Shell.OpenBesideCommand.Execute(b);
        h.Shell.OpenBesideCommand.Execute(c);
        Assert.Equal([a, b, c], h.Shell.Panes);

        // Gamma has the focus, opened last; Beta is the last pane without it.
        h.Shell.OpenBesideCommand.Execute(d);

        Assert.Equal([a, c, d], h.Shell.Panes);
        Assert.Same(d, h.Shell.SelectedTab);
    }

    [Fact]
    public async Task Closing_a_pane_keeps_its_tab_open_and_the_pane_beside_it_takes_the_focus()
    {
        var (h, a, b, c) = await ThreeTabsAsync();
        await using var _ = h;
        h.Shell.OpenBesideCommand.Execute(b);
        h.Shell.OpenBesideCommand.Execute(c);

        c.ClosePaneCommand.Execute(null);

        Assert.Equal([a, b], h.Shell.Panes);
        Assert.Same(b, h.Shell.SelectedTab);
        Assert.Contains(c, h.Shell.AllTabs);
        Assert.False(c.IsShown);

        h.Shell.CloseOtherPanesCommand.Execute(null);
        Assert.Equal([b], h.Shell.Panes);
        Assert.False(b.IsInSplit);
        Assert.Null(h.Services.State.PaneTabIds);
    }

    [Fact]
    public async Task Closing_a_tab_in_a_pane_takes_its_pane_with_it()
    {
        var (h, a, b, _) = await ThreeTabsAsync();
        await using var __ = h;
        h.Shell.OpenBesideCommand.Execute(b);

        await h.Shell.CloseTabCommand.ExecuteAsync(b);

        Assert.Equal([a], h.Shell.Panes);
        Assert.Same(a, h.Shell.SelectedTab);
        Assert.False(a.IsInSplit);
    }

    [Fact]
    public async Task Panes_fold_away_as_the_window_narrows_and_come_back_as_it_widens()
    {
        var (h, a, b, c) = await ThreeTabsAsync();
        await using var _ = h;
        h.Shell.OpenBesideCommand.Execute(b);
        h.Shell.OpenBesideCommand.Execute(c);
        h.Shell.SelectedTab = a;

        // Room for two beside the sidebar: the focused one and its neighbour.
        h.Shell.Layout.SetAvailableWidth(ShellLayout.DefaultSidebarWidth + 2 * ShellLayout.MinConversationWidth + 10);
        Assert.Equal([a, b], h.Shell.Panes);
        Assert.False(c.IsShown);
        h.Shell.SelectedTab = c;
        Assert.Equal([b, c], h.Shell.Panes);

        h.Shell.Layout.SetAvailableWidth(2000);
        Assert.Equal([a, b, c], h.Shell.Panes);
        Assert.True(a.IsShown);
    }

    [Fact]
    public async Task A_tab_on_screen_beside_the_focused_one_isnt_unread_and_doesnt_notify()
    {
        var (h, a, b, _) = await ThreeTabsAsync();
        await using var __ = h;
        h.Shell.OpenBesideCommand.Execute(b);
        h.Services.Notifications.SetAppActive(true);
        h.Shell.SelectedTab = a;
        h.Notifier.Shown.Clear();

        // Beta started second: its pretend process is the second.
        h.Transport.EmitTo(1, ThreadScript.Init());
        h.Transport.EmitTo(1, """{"type":"assistant","message":{"content":[{"type":"text","text":"done"}]}}""");
        h.Transport.EmitTo(1, """{"type":"result","subtype":"success","is_error":false,"session_id":"s","duration_ms":1000}""");
        await TabTestHarness.Eventually(() => b.Status == TabStatus.Idle && b.Items.OfType<Conversation.TurnSummaryItem>().Any(), "the turn");

        Assert.Equal(TabStatus.Idle, b.Status);
        Assert.Null(h.Notifier.Last("TurnFinished:"));
    }

    [Fact]
    public async Task A_thread_shows_beside_its_sub_threads()
    {
        var (h, a, b, c) = await ThreeTabsAsync();
        await using var _ = h;
        h.Shell.MakeThreadCommand.Execute(a);
        b.AssignToThreadCommand.Execute(a);
        c.AssignToThreadCommand.Execute(a);
        Assert.True(a.HasSubThreads);
        h.Shell.SelectedTab = c;

        h.Shell.ShowThreadSideBySideCommand.Execute(c);

        Assert.Equal([a, b, c], h.Shell.Panes);
        Assert.Same(a, h.Shell.SelectedTab);
    }

    [Fact]
    public async Task The_panes_come_back_after_a_restart()
    {
        var (h, a, b, _) = await ThreeTabsAsync();
        await using var __ = h;
        h.Shell.OpenBesideCommand.Execute(b);

        var snapshot = h.Shell.CaptureForRestart();

        Assert.Equal([a.Id, b.Id], snapshot.PaneTabIds);
        Assert.Equal(b.Id, snapshot.SelectedTabId);
    }
}
