using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Claudette.App.Conversation;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.Views;
using LiveMarkdown.Avalonia;

namespace Claudette.App.UiTests;

/// <summary>The conversation rendered (DESIGN.md §5): virtualized, following new output, scrolling to a card, and links.</summary>
public sealed class ConversationUiTests
{
    [AvaloniaFact]
    public async Task A_long_conversation_only_has_controls_for_what_is_in_view_and_follows_new_output()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        for (var i = 0; i < 400; i++)
        {
            tab.Items.Add(new NoteItem($"Note {i}", NoteKind.Info));
        }
        UiText.Settle(window);
        var view = window.GetVisualDescendants().OfType<TabView>().Single();
        var list = view.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Name == "ConversationItems");
        var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "ConversationScroll");
        UiText.Settle(window);

        var realized = list.GetRealizedContainers().Count();
        Assert.InRange(realized, 1, 120);
        // It followed the new output to the end.
        Assert.Equal(scroll.Extent.Height - scroll.Viewport.Height, scroll.Offset.Y, precision: 0);
        Assert.Contains("Note 399", UiText.Describe(view), StringComparison.Ordinal);
        Assert.DoesNotContain("Note 0\n", UiText.Describe(view), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Scrolling_to_a_card_far_up_brings_it_into_view()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        var target = new NoteItem("The card to find", NoteKind.Info);
        tab.Items.Add(target);
        for (var i = 0; i < 400; i++)
        {
            tab.Items.Add(new NoteItem($"Note {i}", NoteKind.Info));
        }
        UiText.Settle(window);
        var view = window.GetVisualDescendants().OfType<TabView>().Single();
        var list = view.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Name == "ConversationItems");
        Assert.Null(list.ContainerFromItem(target));

        Assert.Same(target, tab.TopLevelItemOf(target));
        tab.ScrollTo(target);
        UiText.Settle(window);

        Assert.NotNull(list.ContainerFromItem(target));
        Assert.Contains("The card to find", UiText.Describe(view), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task A_click_on_a_web_link_in_a_reply_opens_it_and_one_to_a_file_doesnt()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.Transport.EmitTurn("[Run this](file:///usr/bin/env)\n\n[The Avalonia docs](https://docs.avaloniaui.net/docs/welcome)");
        var view = window.GetVisualDescendants().OfType<TabView>().Single();
        await UiText.SettleUntilAsync(window, () => Links(view).Count == 2, "the reply's links");
        var (file, web) = (Links(view)[0], Links(view)[1]);

        Click(file);
        Assert.Empty(h.Platform.OpenedUrls);
        Click(web);
        Assert.Equal(["https://docs.avaloniaui.net/docs/welcome"], h.Platform.OpenedUrls);

        void Click(MarkdownTextBlock block)
        {
            var at = block.TranslatePoint(new Point(4, block.Bounds.Height / 2), window)!.Value;
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            UiText.Settle(window);
        }
    }

    /// <summary>The text blocks of the first reply, one per paragraph.</summary>
    private static List<MarkdownTextBlock> Links(TabView view) =>
        [.. view.GetVisualDescendants().OfType<ItemsControl>().Single(i => i.Name == "ConversationItems")
            .GetVisualDescendants().OfType<MarkdownRenderer>().FirstOrDefault()?.GetVisualDescendants().OfType<MarkdownTextBlock>() ?? []];
}
