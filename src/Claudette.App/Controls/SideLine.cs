using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Claudette.App.Controls;

/// <summary>
/// A line down its left edge, as a border's left side would draw, with its content after it and its
/// <see cref="Decorator.Padding"/>, and a gap in it wherever a control <see cref="OnLine"/> centers on it sits, such as a
/// tab row's chevron on its group's line (DESIGN.md §4, "Sidebar"). The gap is left out of the line, not painted over it
/// in the sidebar's color, which wouldn't hide it where that color is see-through, as over Mica on Windows 11.
/// </summary>
public class SideLine : Decorator
{
    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<SideLine, IBrush?>(nameof(LineBrush));

    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<SideLine, double>(nameof(LineThickness), 1);

    /// <summary>Where each control on the line sits, top to bottom, in the line's coordinates.</summary>
    private readonly Dictionary<Control, (double Top, double Bottom)> _gaps = [];

    static SideLine()
    {
        AffectsRender<SideLine>(LineBrushProperty);
        AffectsMeasure<SideLine>(LineThicknessProperty);
    }

    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public double LineThickness
    {
        get => GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    /// <summary>How wide the line is drawn: on whole screen pixels, as a border's side is.</summary>
    internal double DrawnThickness =>
        UseLayoutRounding ? LayoutHelper.RoundLayoutValue(LineThickness, LayoutHelper.GetLayoutScale(this)) : LineThickness;

    /// <summary>Leaves a gap in the line from <paramref name="top"/> to <paramref name="bottom"/>, where <paramref name="control"/> is now.</summary>
    internal void SetGap(Control control, double top, double bottom)
    {
        if (!_gaps.TryGetValue(control, out var gap) || gap != (top, bottom))
        {
            _gaps[control] = (top, bottom);
            InvalidateVisual();
        }
    }

    /// <summary>Closes <paramref name="control"/>'s gap, once it's hidden.</summary>
    internal void ClearGap(Control control)
    {
        if (_gaps.Remove(control))
        {
            InvalidateVisual();
        }
    }

    /// <summary>The line as it's drawn: its pieces, top to bottom, between the gaps.</summary>
    internal IReadOnlyList<Rect> Segments()
    {
        // A control taken away with its row doesn't say so; it's just no longer under the line.
        foreach (var gone in _gaps.Keys.Where(c => !this.IsVisualAncestorOf(c)).ToList())
        {
            _gaps.Remove(gone);
        }
        var width = DrawnThickness;
        var segments = new List<Rect>();
        var from = 0.0;
        foreach (var (top, bottom) in _gaps.Values.OrderBy(g => g.Top))
        {
            if (top > from)
            {
                segments.Add(new Rect(0, from, width, top - from));
            }
            from = Math.Max(from, bottom);
        }
        if (Bounds.Height > from)
        {
            segments.Add(new Rect(0, from, width, Bounds.Height - from));
        }
        return segments;
    }

    public override void Render(DrawingContext context)
    {
        if (LineBrush is not { } brush)
        {
            return;
        }
        foreach (var segment in Segments())
        {
            context.FillRectangle(brush, segment);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var inset = Inset();
        if (Child is not { } child)
        {
            return default(Size).Inflate(inset);
        }
        child.Measure(availableSize.Deflate(inset));
        return child.DesiredSize.Inflate(inset);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize).Deflate(Inset()));
        return finalSize;
    }

    /// <summary>The line, then the padding, before the content.</summary>
    private Thickness Inset() => new Thickness(DrawnThickness, 0, 0, 0) + Padding;
}
