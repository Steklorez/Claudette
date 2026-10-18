using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Claudette.App.Controls;

namespace Claudette.App.UiTests;

/// <summary>
/// Something beside a line of text that never makes the line taller (DESIGN.md §5, "Working line"): it's centred on the
/// line's height, overhanging it when it's taller.
/// </summary>
public class OverhangTests
{
    private sealed class Sized(double width, double height) : Control
    {
        protected override Size MeasureOverride(Size availableSize) => new(width, height);
    }

    private static (StackPanel Line, Sized Held) Show(double heldHeight)
    {
        var held = new Sized(14, heldHeight);
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { new Overhang { Child = held }, new Sized(100, 20) },
        };
        UiText.Show(new Border { Width = 300, Child = line });
        return (line, held);
    }

    [AvaloniaFact]
    public void Taller_than_the_line_it_overhangs_it_centred_and_the_line_keeps_its_height()
    {
        var (line, held) = Show(30);

        Assert.Equal(20, line.Bounds.Height);
        Assert.Equal(new Rect(0, -5, 14, 30), held.Bounds);
    }

    [AvaloniaFact]
    public void Shorter_than_the_line_it_sits_centred_on_it()
    {
        var (line, held) = Show(10);

        Assert.Equal(20, line.Bounds.Height);
        Assert.Equal(new Rect(0, 5, 14, 10), held.Bounds);
    }
}
