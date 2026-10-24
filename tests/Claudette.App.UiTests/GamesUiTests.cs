using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Claudette.App.Games;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.App.Views;

namespace Claudette.App.UiTests;

/// <summary>
/// Games with Claudette rendered (DESIGN.md §18): the side panel's Games page with each board, the keyboard while the
/// page has focus, and the ways in: her menu and the usage limit bar.
/// </summary>
public class GamesUiTests
{
    [AvaloniaFact]
    public async Task The_Games_page_is_made_only_while_a_game_shows_and_plays_tic_tac_toe()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        h.Services.Random = new Random(4);
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        Assert.Empty(window.GetVisualDescendants().OfType<GamesView>());
        Assert.DoesNotContain(PageTabs(window), t => t == "Games");

        tab.PlayGame(GameKind.TicTacToe);
        UiText.Settle(window);

        Assert.Contains("Games", PageTabs(window));
        var page = window.GetVisualDescendants().OfType<GamesView>().Single();
        var squares = Squares(page);
        Assert.Equal(9, squares.Count);
        Assert.Equal("Top left, empty", AutomationProperties.GetName(squares[0]));

        Click(window, squares[4]);
        h.Time.Advance(GameViewModel.LongestThought);
        UiText.Settle(window);

        Assert.Equal("Middle, yours", AutomationProperties.GetName(Squares(page)[4]));
        Assert.Single(Squares(page), s => AutomationProperties.GetName(s)!.EndsWith(", Claudette's", StringComparison.Ordinal));
        var shown = UiText.Describe(page);

        // Another page, and the game's controls go; it's still open, and shows again on its page.
        tab.Page = SidePanelPage.Files;
        UiText.Settle(window);
        Assert.Empty(window.GetVisualDescendants().OfType<GamesView>());
        tab.Page = SidePanelPage.Games;
        UiText.Settle(window);
        page = window.GetVisualDescendants().OfType<GamesView>().Single();
        Assert.Single(Squares(page), s => AutomationProperties.GetName(s)!.EndsWith(", yours", StringComparison.Ordinal));

        // Done playing: the page and its tab go.
        Click(window, page.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Done playing"));
        Assert.DoesNotContain(PageTabs(window), t => t == "Games");
        window.Close();

        await Verify(shown);
    }

    [AvaloniaFact]
    public async Task Hangman_takes_letters_typed_on_the_page_and_none_reach_the_composer()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        h.Services.Random = new Random(9);
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        tab.PlayGame(GameKind.Hangman);
        UiText.Settle(window);
        h.Time.Advance(GameViewModel.LongestThought);
        UiText.Settle(window);
        var page = window.GetVisualDescendants().OfType<GamesView>().Single();
        var game = (HangmanViewModel)h.Services.Games.Game!;
        // Started just now, the page took the keyboard.
        await UiText.SettleUntilAsync(window, () => page.IsKeyboardFocusWithin, "the page to take the keyboard");

        var hit = game.Word![0];
        var miss = Enumerable.Range('A', 26).Select(c => (char)c).First(c => !game.Word.Contains(c));
        window.KeyPressQwerty(Physical(hit), RawInputModifiers.None);
        window.KeyPressQwerty(Physical(miss), RawInputModifiers.None);
        UiText.Settle(window);

        Assert.Equal(hit, game.Pattern[0]);
        Assert.Equal(1, game.Misses);
        Assert.Equal("", tab.ComposerText ?? "");
        Assert.Equal($"{miss}, not in the word", AutomationProperties.GetName(Letter(page, miss)));
        Assert.Equal("The gallows, 1 of 6 misses drawn", AutomationProperties.GetName(page.GetVisualDescendants().OfType<Canvas>().Single()));
        var shown = UiText.Describe(page, (game.Word, "WORD"));
        window.Close();

        await Verify(shown);

        static PhysicalKey Physical(char letter) => PhysicalKey.A + (letter - 'A');

        static Button Letter(GamesView page, char letter) =>
            page.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("gameletter") && b.Content as string == letter.ToString());
    }

    [AvaloniaFact]
    public async Task Connect_four_drops_a_piece_in_the_column_clicked()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        h.Services.Random = new Random(6);
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        tab.PlayGame(GameKind.ConnectFour);
        UiText.Settle(window);
        var page = window.GetVisualDescendants().OfType<GamesView>().Single();
        var columns = page.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("gamecolumn")).ToList();
        Assert.Equal(7, columns.Count);
        Assert.Equal("Column 3, 0 of 6 filled", AutomationProperties.GetName(columns[2]));

        Click(window, columns[2]);
        h.Time.Advance(GameViewModel.LongestThought);
        UiText.Settle(window);

        Assert.Equal("Column 3, 1 of 6 filled", AutomationProperties.GetName(columns[2]));
        var pieces = columns[2].GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().ToList();
        Assert.Contains("yours", pieces[^1].Classes);
        Assert.Single(page.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>(), e => e.Classes.Contains("hers"));
        var shown = UiText.Describe(page);
        window.Close();

        await Verify(shown);
    }

    [AvaloniaFact]
    public async Task Her_menu_offers_the_games_while_theyre_on()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        var layer = window.GetVisualDescendants().OfType<MascotLayer>().Single(l => !l.IsFront);
        var menu = layer.Figure.ContextMenu!;
        Assert.Equal(["Play tic-tac-toe", "Play Connect Four", "Play hangman", "Hide Claudette", "Claudette settings…"],
            menu.Items.OfType<MenuItem>().Where(i => i.IsVisible).Select(i => i.Header as string));

        menu.Items.OfType<MenuItem>().Single(i => i.Header as string == "Play Connect Four").Command!.Execute(GameKind.ConnectFour);
        UiText.Settle(window);
        Assert.IsType<ConnectFourViewModel>(h.Services.Games.Game);
        Assert.True(tab.IsGamesPage);

        h.Services.Settings.Appearance.GamesWithClaudette = false;
        h.Services.SaveSettings();
        UiText.Settle(window);
        Assert.Equal(["Hide Claudette", "Claudette settings…"], menu.Items.OfType<MenuItem>().Where(i => i.IsVisible).Select(i => i.Header as string));
        Assert.DoesNotContain(menu.Items.OfType<Separator>(), s => s.IsVisible);
        Assert.Empty(window.GetVisualDescendants().OfType<GamesView>());
        window.Close();
    }

    private static List<string?> PageTabs(Window window) =>
        [.. window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("pagetab") && b.IsVisible)
            .Select(b => b.GetVisualDescendants().OfType<TextBlock>().First().Text)];

    private static List<Button> Squares(Control page) => [.. page.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("gamesquare"))];

    private static void Click(Window window, Control target)
    {
        var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseMove(center);
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        UiText.Settle(window);
    }
}
