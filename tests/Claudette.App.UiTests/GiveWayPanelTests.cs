using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Claudette.App.Controls;

namespace Claudette.App.UiTests;

/// <summary>
/// A line that gives way from the first (DESIGN.md §5, "Working line"): side by side while they fit, and when they don't,
/// the first child trims before the ones after it, so the line stays within its width.
/// </summary>
public class GiveWayPanelTests
{
    /// <summary>As wide as it asks for, or as wide as it's given when that's less, as a trimmed TextBlock is.</summary>
    private sealed class Trims(double width) : Control
    {
        protected override Size MeasureOverride(Size availableSize) => new(Math.Min(width, availableSize.Width), 20);
    }

    private static (GiveWayPanel Line, Trims First, Trims Second, Trims Third) Show(double width, bool secondShown = true)
    {
        var (first, second, third) = (new Trims(200), new Trims(100) { IsVisible = secondShown }, new Trims(50));
        var line = new GiveWayPanel { Spacing = 10, Children = { first, second, third } };
        UiText.Show(new Border { Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = line });
        return (line, first, second, third);
    }

    [AvaloniaFact]
    public void With_room_the_children_sit_side_by_side_at_their_own_widths()
    {
        var (_, first, second, third) = Show(500);

        Assert.Equal(new Rect(0, 0, 200, 20), first.Bounds);
        Assert.Equal(new Rect(210, 0, 100, 20), second.Bounds);
        Assert.Equal(new Rect(320, 0, 50, 20), third.Bounds);
    }

    [AvaloniaFact]
    public void Without_room_the_first_gives_way_and_the_rest_keep_their_widths()
    {
        var (line, first, second, third) = Show(300);

        Assert.Equal(new Rect(0, 0, 130, 20), first.Bounds);
        Assert.Equal(new Rect(140, 0, 100, 20), second.Bounds);
        Assert.Equal(new Rect(250, 0, 50, 20), third.Bounds);
        Assert.Equal(300, line.Bounds.Width);
    }

    [AvaloniaFact]
    public void When_the_first_has_given_all_it_can_the_next_gives_way()
    {
        var (_, first, second, third) = Show(120);

        Assert.Equal(0, first.Bounds.Width);
        Assert.Equal(new Rect(10, 0, 50, 20), second.Bounds);
        Assert.Equal(new Rect(70, 0, 50, 20), third.Bounds);
    }

    [AvaloniaFact]
    public void A_hidden_child_takes_no_room_and_no_spacing()
    {
        var (_, first, _, third) = Show(300, secondShown: false);

        Assert.Equal(new Rect(0, 0, 200, 20), first.Bounds);
        Assert.Equal(new Rect(210, 0, 50, 20), third.Bounds);
    }
}
