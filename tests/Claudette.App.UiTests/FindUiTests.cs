using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Claudette.App.Conversation;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.Views;

namespace Claudette.App.UiTests;

/// <summary>Find in the conversation (DESIGN.md §5, "Find").</summary>
public class FindUiTests
{
    [AvaloniaFact]
    public async Task The_find_shortcut_opens_the_bar_and_Enter_and_Esc_work_in_it()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell }, 1100, 800);
        foreach (var text in new[] { "Fix the parser", "Now the lexer", "And the parser tests" })
        {
            tab.ComposerText = text;
            await tab.SendCommand.ExecuteAsync(null);
            h.Transport.EmitTurn("done");
            await UiText.SettleUntilAsync(window, () => tab.Status == ViewModels.TabStatus.Idle && tab.IsSettled, "the turn");
        }
        var primary = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

        window.KeyPressQwerty(PhysicalKey.F, primary);
        window.KeyReleaseQwerty(PhysicalKey.F, primary);
        UiText.Settle(window);
        var box = window.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "FindBox");
        Assert.True(tab.Find.IsOpen);
        Assert.True(box.IsEffectivelyVisible);
        Assert.True(box.IsFocused);

        window.KeyTextInput("parser");
        UiText.Settle(window);
        Assert.Equal("2 of 2", tab.Find.CountText);
        var newest = tab.Items.OfType<UserMessageItem>().Last();
        Assert.Same(newest, tab.Find.Current);
        // The match the bar shows is marked.
        Assert.Equal([newest], Marked(window));

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        UiText.Settle(window);
        Assert.Equal("1 of 2", tab.Find.CountText);
        Assert.Equal([tab.Items.OfType<UserMessageItem>().First()], Marked(window));

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        UiText.Settle(window);
        Assert.False(tab.Find.IsOpen);
        Assert.Empty(Marked(window));
        Assert.True(window.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "Composer").IsFocused);
    }

    [AvaloniaFact]
    public async Task The_text_found_is_marked_where_it_is_in_the_card_brought_into_view()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell }, 1100, 800);
        foreach (var (prompt, reply) in new[] { ("Fix the parser", "The parser now reads **parser** directives, and the parser tests pass."), ("Thanks", "Done.") })
        {
            tab.ComposerText = prompt;
            await tab.SendCommand.ExecuteAsync(null);
            h.Transport.EmitTurn(reply);
            await UiText.SettleUntilAsync(window, () => tab.Status == ViewModels.TabStatus.Idle && tab.IsSettled, "the turn");
        }
        var marks = window.GetVisualDescendants().OfType<Controls.FindHighlights>().Single(m => m.Name == "FindMarks");

        tab.Find.IsOpen = true;
        tab.Find.Query = "parser";

        // The newest match is the reply: each of its three, the bold one included, marked where it is in the card.
        Assert.IsType<AssistantTextItem>(tab.Find.Current);
        await UiText.SettleUntilAsync(window, () => marks.Rects.Count == 3, "the reply's marks");
        var card = window.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Classes.Contains("findcurrent"));
        var cardRect = new Rect(card.TranslatePoint(default, marks)!.Value, card.Bounds.Size);
        Assert.All(marks.Rects, r => Assert.True(cardRect.Contains(r), $"A mark at {r} is outside the card at {cardRect}."));
        Assert.All(marks.Rects, r => Assert.True(r.Width > 0 && r.Height > 0));

        // The match before it is the prompt, with the text once.
        tab.Find.PreviousCommand.Execute(null);
        await UiText.SettleUntilAsync(window, () => marks.Rects.Count == 1 && tab.Find.Current is UserMessageItem, "the prompt's mark");

        tab.Find.IsOpen = false;
        await UiText.SettleUntilAsync(window, () => marks.Rects.Count == 0, "no marks once Find closes");
        window.Close();
    }

    [AvaloniaFact]
    public void Text_scrolled_out_of_sight_in_its_own_box_isnt_marked()
    {
        // As a tool's output in its card: a box that scrolls, with the text once at its top and once below what the box
        // shows, yet inside the window, so only the box's own clipping hides it.
        var output = new SelectableTextBlock { Text = "needle at the top\n" + string.Concat(Enumerable.Repeat("filler\n", 8)) + "needle at the bottom" };
        var box = new ScrollViewer { MaxHeight = 60, Content = output, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var marks = new Controls.FindHighlights { Fill = Avalonia.Media.Brushes.Yellow };
        var window = UiText.Show(new Panel { Children = { box, marks } }, 400, 300);

        marks.Show(() => box, "needle");
        UiText.Settle(window);

        var mark = Assert.Single(marks.Rects);
        Assert.True(mark.Bottom <= 60, $"The mark at {mark} is below the box.");
        window.Close();
    }

    private static List<object?> Marked(Window window) =>
        [.. window.GetVisualDescendants().OfType<ContentPresenter>().Where(p => p.Classes.Contains("findcurrent")).Select(p => p.Content)];
}
