using Avalonia;
using Avalonia.Controls;

namespace Claudette.App.Controls;

/// <summary>
/// Children side by side on one line, left to right, as a horizontal stack panel lays them out while they fit. When they
/// don't, the first gives way first: each child gets at most what the ones after it leave, so a long verb or path trims
/// (its TextBlock's <c>TextTrimming</c>) before the shorter things after it, and the line never runs past its end into
/// whatever is beside it (DESIGN.md §5, "Working line"). A child that can't get narrower than it asked still overruns.
/// </summary>
public sealed class GiveWayPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<GiveWayPanel, double>(nameof(Spacing));

    private readonly List<double> _widths = [];

    static GiveWayPanel()
    {
        AffectsMeasure<GiveWayPanel>(SpacingProperty);
    }

    /// <summary>The space between children.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private List<Control> Shown => Children.Where(c => c.IsVisible).ToList();

    protected override Size MeasureOverride(Size availableSize)
    {
        var shown = Shown;
        _widths.Clear();
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var child in Children)
        {
            child.Measure(unbounded);
        }
        var room = availableSize.Width - Spacing * Math.Max(0, shown.Count - 1);
        if (shown.Sum(c => c.DesiredSize.Width) > room)
        {
            // From the last, each child keeps what it asked for as far as there's room, and the first gets what's left.
            for (var i = shown.Count - 1; i >= 0; i--)
            {
                var child = shown[i];
                var width = Math.Max(0, Math.Min(child.DesiredSize.Width, room));
                if (width < child.DesiredSize.Width)
                {
                    child.Measure(new Size(width, availableSize.Height));
                }
                room -= child.DesiredSize.Width;
            }
        }
        _widths.AddRange(shown.Select(c => c.DesiredSize.Width));
        return new Size(
            _widths.Sum() + Spacing * Math.Max(0, shown.Count - 1),
            shown.Count == 0 ? 0 : shown.Max(c => c.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var shown = Shown;
        var x = 0.0;
        for (var i = 0; i < shown.Count; i++)
        {
            var width = i < _widths.Count ? _widths[i] : shown[i].DesiredSize.Width;
            shown[i].Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + Spacing;
        }
        return finalSize;
    }
}
