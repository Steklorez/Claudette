using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;

namespace Claudette.App.Controls;

/// <summary>
/// The lines between the tabs side by side, to drag (DESIGN.md §18, "Split panes"): laid over the panes, it takes the
/// pointer only a few pixels either side of a line, so everywhere else reaches the panes under it. While one is
/// pointed at or dragged, it's drawn in the accent.
/// </summary>
public sealed class PaneDividers : Control, ICustomHitTest
{
    public static readonly StyledProperty<IReadOnlyList<double>> LinesProperty =
        AvaloniaProperty.Register<PaneDividers, IReadOnlyList<double>>(nameof(Lines), []);

    public static readonly StyledProperty<IBrush?> HighlightProperty =
        AvaloniaProperty.Register<PaneDividers, IBrush?>(nameof(Highlight));

    /// <summary>
    /// How far either side of a line still takes the pointer: more to its right, the next pane's quiet margin, than its
    /// left, where the pane before has its scroll bar.
    /// </summary>
    private const double ReachLeft = 2, ReachRight = 5;

    private static readonly Cursor Resize = new(StandardCursorType.SizeWestEast);

    // The line pointed at, and the one being dragged; -1 for none.
    private int _hover = -1;
    private int _dragging = -1;

    // How far right of the line it was taken hold of, so it doesn't jump to the pointer.
    private double _grab;

    static PaneDividers() => AffectsRender<PaneDividers>(LinesProperty, HighlightProperty);

    public PaneDividers() => Cursor = Resize;

    /// <summary>Where the lines are, from the left.</summary>
    public IReadOnlyList<double> Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    /// <summary>The line pointed at, or dragged.</summary>
    public IBrush? Highlight
    {
        get => GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    /// <summary>A line is being dragged: its number from the left, and where the pointer is.</summary>
    public event Action<int, double>? Dragged;

    /// <summary>The drag ended: what it left is to be kept.</summary>
    public event Action? DragEnded;

    /// <summary>A line was double-clicked: the panes share the width evenly again.</summary>
    public event Action? ResetRequested;

    /// <summary>The line within reach of <paramref name="x"/>; -1 with none.</summary>
    internal int LineAt(double x)
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            if (x >= Lines[i] - ReachLeft && x <= Lines[i] + ReachRight)
            {
                return i;
            }
        }
        return -1;
    }

    public bool HitTest(Point point) => _dragging >= 0 || point.Y >= 0 && point.Y <= Bounds.Height && LineAt(point.X) >= 0;

    public override void Render(DrawingContext context)
    {
        var line = _dragging >= 0 ? _dragging : _hover;
        if (line >= 0 && line < Lines.Count && Highlight is { } brush)
        {
            context.FillRectangle(brush, new Rect(Lines[line] - 1, 0, 2, Bounds.Height));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var x = e.GetPosition(this).X;
        if (_dragging >= 0)
        {
            Dragged?.Invoke(_dragging, x - _grab);
            e.Handled = true;
            return;
        }
        SetHover(LineAt(x));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_dragging < 0)
        {
            SetHover(-1);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var line = LineAt(e.GetPosition(this).X);
        if (line < 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        if (e.ClickCount == 2)
        {
            ResetRequested?.Invoke();
        }
        else
        {
            _dragging = line;
            _grab = e.GetPosition(this).X - Lines[line];
            e.Pointer.Capture(this);
            InvalidateVisual();
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging >= 0)
        {
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_dragging >= 0)
        {
            _dragging = -1;
            InvalidateVisual();
            DragEnded?.Invoke();
        }
    }

    private void SetHover(int line)
    {
        if (line != _hover)
        {
            _hover = line;
            InvalidateVisual();
        }
    }
}
