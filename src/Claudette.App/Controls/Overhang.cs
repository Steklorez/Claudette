using Avalonia;
using Avalonia.Controls;

namespace Claudette.App.Controls;

/// <summary>
/// Holds its child beside a line of text without making the line taller: it asks for the child's width and none of its
/// height, and centres the child on whatever height the line has, overhanging it if the child is taller. The working
/// line's glyph sits in one (DESIGN.md §5, "Working line"). Each frame comes from whichever font has it, and Avalonia
/// looks for one among the fonts already in use first, so one frame could come out taller than the rest (✶ in Consolas on
/// Windows, once a code block had shown) and lift the line, and the conversation with it, as the glyph twinkled.
/// </summary>
public sealed class Overhang : Decorator
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } child)
        {
            return default;
        }
        child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new Size(child.DesiredSize.Width, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is { } child)
        {
            var height = child.DesiredSize.Height;
            child.Arrange(new Rect(0, (finalSize.Height - height) / 2, finalSize.Width, height));
        }
        return finalSize;
    }
}
