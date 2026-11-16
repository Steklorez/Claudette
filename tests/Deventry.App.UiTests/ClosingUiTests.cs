using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.App.Views;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.App.UiTests;

/// <summary>
/// The card over the window while Deventry closes (DESIGN.md §13, "What closing does, on screen"): its title, its bar,
/// the step it's on, and Joulene waving goodbye from its top edge.
/// </summary>
public class ClosingUiTests
{
    [AvaloniaFact]
    public async Task The_card_says_what_closing_is_on_with_Joulene_standing_on_it()
    {
        using var closing = new ClosingViewModel(new FakeTimeProvider(), new InlineDispatcher(), "Closing Deventry…", 5,
            new ClosingJoulene(Still: true, Hat: null, PixelsPerCell: 3), TimeSpan.Zero);
        closing.TellingBots(["Telegram"]);
        closing.Advance();
        closing.StoppingTabs(["Grappling hook", "Stamina", "Inventory"]);
        closing.TabStopped();
        var view = new ClosingView { DataContext = closing };
        var window = UiText.Show(view, 600, 400);
        UiText.Settle(window);

        // She stands on the card's top edge: her figure as tall as her sprite, in her cells, above the card.
        var stand = view.FindControl<Panel>("Stand")!;
        var figure = view.GetVisualDescendants().OfType<MascotFigure>().Single();
        Assert.True(stand.IsVisible);
        Assert.Equal(("wave", 36.0), (figure.Frame?.Pose, figure.Bounds.Height));
        Assert.Equal(stand.Bounds.Bottom, view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("dialog")).Bounds.Top);

        await Verify(UiText.Describe(window));
    }
}
