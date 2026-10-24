using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Claudette.App.Controls;

namespace Claudette.App.UiTests;

/// <summary>
/// A line down the left with a gap wherever a control on it sits, such as a tab row's chevron on its group's line
/// (Controls/SideLine and Controls/OnLine; DESIGN.md §4, "Sidebar").
/// </summary>
public class SideLineTests
{
    [AvaloniaFact]
    public void Its_content_comes_after_the_line_and_its_padding()
    {
        var (_, line, rows, _) = Line();

        Assert.Equal(2, line.DrawnThickness);
        Assert.Equal(7, rows.Children[0].TranslatePoint(default, line)!.Value.X);
        Assert.Equal(new Size(7 + 100, 3 * 30), line.DesiredSize);
    }

    [AvaloniaFact]
    public void The_line_leaves_a_gap_where_a_control_on_it_sits_and_closes_it_when_the_control_goes()
    {
        var (window, line, rows, mark) = Line();

        Assert.Equal(1, mark.TranslatePoint(new Point(mark.Bounds.Width / 2, 0), line)!.Value.X);
        AssertGaps(line, mark);

        // Hidden, it leaves none.
        mark.IsVisible = false;
        UiText.Settle(window);
        AssertGaps(line);

        // Shown again in another row, its gap is there.
        ((Panel)rows.Children[0]).Children.Remove(mark);
        ((Panel)rows.Children[2]).Children.Add(mark);
        mark.IsVisible = true;
        UiText.Settle(window);
        Assert.Equal(1, mark.TranslatePoint(new Point(mark.Bounds.Width / 2, 0), line)!.Value.X);
        AssertGaps(line, mark);

        // Taken away with its row, it leaves none.
        rows.Children.RemoveAt(2);
        UiText.Settle(window);
        AssertGaps(line);
    }

    /// <summary>
    /// Checks that <paramref name="line"/> is drawn from top to bottom but for a gap as tall as each of
    /// <paramref name="on"/>, where it sits.
    /// </summary>
    internal static void AssertGaps(SideLine line, params Control[] on)
    {
        var expected = new List<Rect>();
        var from = 0.0;
        foreach (var control in on.OrderBy(c => c.TranslatePoint(default, line)!.Value.Y))
        {
            var top = control.TranslatePoint(default, line)!.Value.Y;
            expected.Add(new Rect(0, from, line.DrawnThickness, top - from));
            from = top + control.Bounds.Height;
        }
        expected.Add(new Rect(0, from, line.DrawnThickness, line.Bounds.Height - from));
        Assert.Equal(expected, line.Segments());
    }

    /// <summary>Three rows under a line, the first with a mark on the line, as a tab row's chevron is.</summary>
    private static (Window Window, SideLine Line, StackPanel Rows, Border Mark) Line()
    {
        var mark = new Border { Width = 14, Height = 20, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        OnLine.SetLine(mark, "Line");
        var rows = new StackPanel();
        for (var i = 0; i < 3; i++)
        {
            rows.Children.Add(new Panel { Width = 100, Height = 30 });
        }
        ((Panel)rows.Children[0]).Children.Add(mark);
        var line = new SideLine
        {
            Name = "Line", LineBrush = Brushes.SeaGreen, LineThickness = 2, Padding = new Thickness(5, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = rows,
        };
        return (UiText.Show(line, 400, 200), line, rows, mark);
    }
}
