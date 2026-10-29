using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Claudette.App.Controls;
using Claudette.App.Conversation;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.Views;

namespace Claudette.App.UiTests;

/// <summary>The conversation as cards, rendered (DESIGN.md §18, "Conversation cards").</summary>
public sealed class ConversationCardsUiTests
{
    [Fact]
    public void Tiles_go_three_across_then_two_then_one_as_the_card_narrows()
    {
        var panel = new TilesPanel();

        Assert.Equal(3, panel.ColumnsFor(820));
        Assert.Equal(2, panel.ColumnsFor(600));
        Assert.Equal(1, panel.ColumnsFor(300));
        Assert.Equal(1, panel.ColumnsFor(double.PositiveInfinity));
    }

    [AvaloniaFact]
    public async Task A_run_of_calls_is_one_card_of_tiles_and_a_tile_opens_its_detail_and_its_window()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.Transport.Emit("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"b1","name":"Bash","input":{"command":"ls docs"}},{"type":"tool_use","id":"b2","name":"Bash","input":{"command":"git status"}}]}}""");
        h.Transport.Emit("""{"type":"user","tool_use_result":{"stdout":"README.md","stderr":""},"message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"b1","content":"README.md"}]}}""");
        await UiText.SettleUntilAsync(window, () => Tiles(window).Count == 2, "the tiles");

        var card = window.GetVisualDescendants().OfType<Card>().Single(c => c.Classes.Contains("tools"));
        Assert.Equal("Tool calls", card.Label);
        Assert.Equal("2 calls · 1 running", card.Detail);
        var (first, second) = (Tiles(window)[0], Tiles(window)[1]);
        // Side by side, on one row of the card.
        var (left, right) = (first.TranslatePoint(default, card)!.Value, second.TranslatePoint(default, card)!.Value);
        Assert.Equal(left.Y, right.Y);
        Assert.True(right.X >= left.X + first.Bounds.Width, $"The second tile starts at {right.X}, inside the first.");
        Assert.Equal("Bash ls docs", AutomationProperties.GetName(first));
        // Its tip has the whole command, and is wider than a tip of words, so a long command wraps less.
        ToolTip.SetIsOpen(first, true);
        UiText.Settle(window);
        var tip = Assert.IsType<ToolTip>(ToolTip.GetTip(first));
        Assert.Equal(("ls docs", 760d), (tip.Content as string, tip.MaxWidth));
        ToolTip.SetIsOpen(first, false);
        var detail = card.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("tiledetail"));
        Assert.False(detail.IsVisible);

        first.Command!.Execute(first.CommandParameter);
        UiText.Settle(window);

        Assert.True(detail.IsEffectivelyVisible);
        Assert.Contains("open", first.Classes);
        Assert.Contains("$ ls docs", UiText.Describe(detail), StringComparison.Ordinal);
        Assert.Contains("README.md", UiText.Describe(detail), StringComparison.Ordinal);

        // Its window follows the call; opening it again brings the same window back.
        var open = detail.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Open in a window");
        open.Command!.Execute(open.CommandParameter);
        UiText.Settle(window);
        var toolWindow = Assert.IsType<ToolCallWindow>(Assert.Single(window.OwnedWindows));
        Assert.Equal("Bash · ls docs", toolWindow.Title);
        open.Command!.Execute(open.CommandParameter);
        UiText.Settle(window);
        Assert.Single(window.OwnedWindows);
        toolWindow.Close();
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_reply_Claude_carried_on_after_is_quieter_until_its_pointed_at_and_the_setting_turns_that_off()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        h.Transport.Emit("""{"type":"assistant","message":{"content":[{"type":"text","text":"Looking first."},{"type":"tool_use","id":"r1","name":"Read","input":{"file_path":"a.cs"}}]}}""");
        h.Transport.Emit("""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"r1","content":"x"}]}}""");
        h.Transport.EmitTurn("All done.");
        await UiText.SettleUntilAsync(window, () => Replies(window).Count == 2, "the replies");
        var (step, answer) = (Replies(window)[0], Replies(window)[1]);

        Assert.Contains("quiet", step.Classes);
        Assert.DoesNotContain("quiet", answer.Classes);
        Assert.Equal(Brushes.Transparent, Part(step).Background);
        Assert.Same(answer.TintBrush, Part(answer).Background);

        window.MouseMove(step.TranslatePoint(new Point(40, step.Bounds.Height / 2), window)!.Value);
        UiText.Settle(window);
        Assert.Same(step.TintBrush, Part(step).Background);

        window.MouseMove(new Point(5, 5));
        h.Services.Settings.Appearance.QuietSteps = false;
        h.Services.SaveSettings();
        UiText.Settle(window);
        Assert.Same(step.TintBrush, Part(step).Background);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Marks_in_the_scroll_bar_go_to_your_messages_and_F3_and_F4_step_between_messages()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        var first = new UserMessageItem("The first question");
        tab.Items.Add(first);
        for (var i = 0; i < 150; i++)
        {
            tab.Items.Add(new NoteItem($"Note {i}", NoteKind.Info));
        }
        var second = new UserMessageItem("The second question");
        tab.Items.Add(second);
        for (var i = 0; i < 150; i++)
        {
            tab.Items.Add(new NoteItem($"Later note {i}", NoteKind.Info));
        }
        UiText.Settle(window);
        var view = window.GetVisualDescendants().OfType<TabView>().Single();
        var list = view.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Name == "ConversationItems");
        var marks = view.GetVisualDescendants().OfType<ScrollMarks>().Single(m => m.Name == "ScrollMarks");
        await UiText.SettleUntilAsync(window, () => marks.Marks.Count == 2, "a mark for each message");
        Assert.Equal([0, 151], marks.Marks.Select(m => m.Index));
        Assert.Equal("Your message: The first question", marks.Marks[0].Tip);
        Assert.True(marks.Marks[0].Fraction < marks.Marks[1].Fraction);
        Assert.Null(list.ContainerFromItem(first));

        // The marks are in the conversation's scroll bar, as wide as its thumb is drawn: thin while the bar rests, scaled from
        // its right edge, and its whole width once the bar widens.
        var bar = view.GetVisualDescendants().OfType<ScrollBar>().First(b => b.Orientation == Orientation.Vertical && b.IsEffectivelyVisible);
        var onFirst = marks.PointOf(marks.Marks[0]);
        Assert.True(new Rect(bar.Bounds.Size).Contains(marks.TranslatePoint(onFirst, bar)!.Value), "The mark is outside the scroll bar.");
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        double Drawn() => Math.Abs(thumb.TranslatePoint(new Point(thumb.Bounds.Width, 0), marks)!.Value.X - thumb.TranslatePoint(default, marks)!.Value.X);
        Assert.InRange(marks.Lane().Width, Drawn() - 0.5, Drawn() + 0.5);
        Assert.True(marks.Lane().Width < bar.Bounds.Width, "The resting bar's marks are as wide as the whole bar.");
        var (restingWidth, resting) = (marks.Lane().Width, thumb.RenderTransform);
        // Widened, as the pointer near it does, and back: Fluent animates the thumb, and the marks keep up.
        thumb.RenderTransform = null;
        await UiText.SettleUntilAsync(window, () => Math.Abs(marks.Lane().Width - bar.Bounds.Width) < 0.5, "the marks as wide as the bar");
        thumb.RenderTransform = resting;
        await UiText.SettleUntilAsync(window, () => Math.Abs(marks.Lane().Width - restingWidth) < 0.5, "the marks thin again");
        onFirst = marks.PointOf(marks.Marks[0]);

        // A click on the first mark goes to the first message.
        var at = marks.TranslatePoint(onFirst, window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        await UiText.SettleUntilAsync(window, () => list.ContainerFromItem(first) is not null, "the first message");

        // There, the thumb is over the first mark, and takes the pointer to be dragged.
        await UiText.SettleUntilAsync(window, () => !marks.HitTest(marks.PointOf(marks.Marks[0])), "the thumb over the first mark");
        Assert.True(marks.HitTest(marks.PointOf(marks.Marks[1])));

        // F4 goes on to the next message, F3 back, Ctrl+F4 to the latest.
        window.KeyPressQwerty(PhysicalKey.F4, RawInputModifiers.None);
        await UiText.SettleUntilAsync(window, () => list.ContainerFromItem(second) is { } shown && IsAtTheTop(shown, view), "the second message");
        window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);
        await UiText.SettleUntilAsync(window, () => list.ContainerFromItem(first) is { } shown && IsAtTheTop(shown, view), "back to the first");
        window.KeyPressQwerty(PhysicalKey.F4, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);
        await UiText.SettleUntilAsync(window, () => UiText.Describe(view).Contains("Later note 149", StringComparison.Ordinal), "the latest");
        window.Close();
    }

    [AvaloniaFact]
    public async Task An_open_thinking_card_ends_where_the_thinking_does_whatever_blank_lines_it_ends_with()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        const string thought = "The tests pass, so I should write up a short report and check whether it was pushed.";
        foreach (var text in new[] { thought, "\n" + thought + "\n\n\n" })
        {
            var thinking = new ThinkingItem { IsExpanded = true };
            thinking.Append(text);
            thinking.IsStreaming = false;
            tab.Items.Add(thinking);
        }
        UiText.Settle(window);

        var cards = window.GetVisualDescendants().OfType<Card>().Where(c => c.Classes.Contains("thinking")).ToList();
        Assert.Equal(2, cards.Count);
        // Claude's thinking often starts or ends with blank lines: they leave no empty room in the card.
        Assert.Equal(cards[0].Bounds.Height, cards[1].Bounds.Height);
        Assert.Contains(thought, UiText.Describe(cards[1]), StringComparison.Ordinal);
        window.Close();
    }

    private static bool IsAtTheTop(Control container, TabView view)
    {
        var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "ConversationScroll");
        var top = container.TranslatePoint(default, scroll)!.Value.Y;
        return top is >= 0 and < 40;
    }

    private static List<Button> Tiles(Window window) =>
        [.. window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("tile"))];

    private static List<Card> Replies(Window window) =>
        [.. window.GetVisualDescendants().OfType<Card>().Where(c => c.Classes.Contains("claude"))];

    private static Border Part(Card card) => card.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Card");
}
