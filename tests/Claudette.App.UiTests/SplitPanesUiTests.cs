using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.Views;

namespace Claudette.App.UiTests;

/// <summary>Tabs side by side, rendered (DESIGN.md §18, "Split panes").</summary>
public sealed class SplitPanesUiTests
{
    [AvaloniaFact]
    public async Task Two_tabs_sit_side_by_side_with_headers_and_a_click_in_one_focuses_it()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        h.Factory.ProcessPerSession = true;
        var alpha = await ThreadScript.OpenAsync(h, "Alpha");
        var beta = await ThreadScript.OpenAsync(h, "Beta");
        h.Shell.SelectedTab = alpha;
        alpha.IsSidePanelOpen = true;
        beta.IsSidePanelOpen = true;
        var window = UiText.Show(new ShellView { DataContext = h.Shell }, 1600, 800);

        h.Shell.OpenBesideCommand.Execute(beta);
        UiText.Settle(window);

        var views = window.GetVisualDescendants().OfType<TabView>().Where(v => v.IsEffectivelyVisible).ToList();
        Assert.Equal(2, views.Count);
        var (left, right) = (views.Single(v => v.DataContext == alpha), views.Single(v => v.DataContext == beta));
        var (leftAt, rightAt) = (left.TranslatePoint(default, window)!.Value, right.TranslatePoint(default, window)!.Value);
        Assert.Equal(leftAt.Y, rightAt.Y);
        Assert.True(rightAt.X >= leftAt.X + left.Bounds.Width - 1, "Beta's pane overlaps Alpha's.");
        // The same width, but for the line between them, which the second pane draws.
        Assert.InRange(left.Bounds.Width - right.Bounds.Width, 0, 1.5);

        // Each pane names its tab; the focused one, Beta, has the accent under its header and the side panel.
        Assert.True(Header(left).IsVisible && Header(right).IsVisible);
        Assert.Contains("Alpha", UiText.Describe(Header(left)), StringComparison.Ordinal);
        Assert.Contains("focused", Header(right).Classes);
        Assert.DoesNotContain("focused", Header(left).Classes);
        Assert.False(SidePanel(left).IsVisible);
        Assert.True(SidePanel(right).IsVisible);

        // A click in Alpha's conversation moves the focus there, and the side panel with it.
        var conversation = left.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "ConversationScroll");
        var at = conversation.TranslatePoint(new Point(conversation.Bounds.Width / 2, 40), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        UiText.Settle(window);

        Assert.Same(alpha, h.Shell.SelectedTab);
        Assert.Contains("focused", Header(left).Classes);
        Assert.True(SidePanel(left).IsVisible);
        Assert.False(SidePanel(right).IsVisible);
        Assert.True(beta.IsShown);

        // Close pane, on Beta's header: Alpha has the window to itself again, without a header.
        var close = Header(right).GetVisualDescendants().OfType<Button>().Single();
        close.Command!.Execute(close.CommandParameter);
        UiText.Settle(window);
        Assert.Single(window.GetVisualDescendants().OfType<TabView>(), v => v.IsEffectivelyVisible);
        Assert.False(Header(left).IsVisible);
        window.Close();
    }

    private static Border Header(TabView view) => view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PaneHeader");

    private static Border SidePanel(TabView view) => view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "SidePanel");
}
