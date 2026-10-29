using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.VisualTree;

namespace Claudette.App.Controls;

/// <summary>A mark in the scroll bar: where its item is, as a part of the conversation's height, and what it is.</summary>
/// <param name="Index">The item's place in the conversation.</param>
public sealed record ScrollMark(double Fraction, IBrush Brush, int Index, string Tip);

/// <summary>
/// Marks in the conversation's scroll bar where the user's messages, waiting prompts, errors and changed files are
/// (DESIGN.md §18, "Conversation cards"), as Rider and VS Code mark theirs: in a lane at the bar's right, from its top to
/// its bottom, over the track and the thumb. A click on one goes there. It lies over the whole of its <see cref="Viewer"/>,
/// but only the marks take the pointer, and not where the thumb is, so the thumb still drags, the track still pages, and
/// the conversation under it still scrolls and selects.
/// </summary>
public sealed class ScrollMarks : Control, ICustomHitTest
{
    public static readonly StyledProperty<IReadOnlyList<ScrollMark>> MarksProperty =
        AvaloniaProperty.Register<ScrollMarks, IReadOnlyList<ScrollMark>>(nameof(Marks), []);

    public static readonly StyledProperty<ScrollViewer?> ViewerProperty =
        AvaloniaProperty.Register<ScrollMarks, ScrollViewer?>(nameof(Viewer));

    /// <summary>The lane's width, at the scroll bar's right.</summary>
    public const double LaneWidth = 6;

    /// <summary>A mark's height; marks closer than this draw over each other.</summary>
    private const double MarkHeight = 3;

    /// <summary>How far from a mark the pointer still points at it.</summary>
    private const double Reach = 4;

    private static readonly Cursor Hand = new(StandardCursorType.Hand);

    /// <summary>The viewer's vertical scroll bar, once its template has made one, which the marks are drawn in.</summary>
    private ScrollBar? _bar;

    static ScrollMarks() => AffectsRender<ScrollMarks>(MarksProperty, ViewerProperty);

    public IReadOnlyList<ScrollMark> Marks
    {
        get => GetValue(MarksProperty);
        set => SetValue(MarksProperty, value);
    }

    /// <summary>The scroll viewer whose vertical scroll bar the marks are drawn in.</summary>
    public ScrollViewer? Viewer
    {
        get => GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>A mark was clicked: the conversation goes to its item.</summary>
    public event Action<ScrollMark>? MarkClicked;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ViewerProperty)
        {
            Watch(null);
        }
    }

    /// <summary>The viewer's vertical scroll bar, found again once the one before has left the window.</summary>
    private ScrollBar? Bar
    {
        get
        {
            if (_bar is { } bar && bar.IsAttachedToVisualTree())
            {
                return bar;
            }
            Watch(Viewer?.GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(b => b.Orientation == Orientation.Vertical));
            return _bar;
        }
    }

    /// <summary>Draws again as the bar moves or changes size, with the pane, or as it shows and hides.</summary>
    private void Watch(ScrollBar? bar)
    {
        if (_bar is not null)
        {
            _bar.LayoutUpdated -= OnBarLaidOut;
        }
        _bar = bar;
        if (_bar is not null)
        {
            _bar.LayoutUpdated += OnBarLaidOut;
        }
    }

    private void OnBarLaidOut(object? sender, EventArgs e) => InvalidateVisual();

    /// <summary>
    /// The lane the marks are in, in this control's coordinates: the bar's right, as tall as its track, between its arrows,
    /// where the thumb goes, so a mark is where the thumb is when its item is at the top. Empty without a bar showing.
    /// </summary>
    private Rect Lane()
    {
        if (Bar is not { IsEffectivelyVisible: true } bar || bar.TranslatePoint(default, this) is not { } at)
        {
            return default;
        }
        var track = bar.GetVisualDescendants().OfType<Track>().FirstOrDefault();
        var (top, height) = track?.TranslatePoint(default, this) is { } trackAt ? (trackAt.Y, track.Bounds.Height) : (at.Y, bar.Bounds.Height);
        return new Rect(at.X + bar.Bounds.Width - LaneWidth, top, LaneWidth, height);
    }

    private static double YOf(ScrollMark mark, Rect lane) =>
        lane.Y + Math.Clamp(mark.Fraction * lane.Height, 0, Math.Max(0, lane.Height - MarkHeight));

    /// <summary>Where <paramref name="mark"/>'s middle is, in this control's coordinates.</summary>
    internal Point PointOf(ScrollMark mark)
    {
        var lane = Lane();
        return new Point(lane.Center.X, YOf(mark, lane) + MarkHeight / 2);
    }

    /// <summary>The mark nearest <paramref name="point"/>, within reach, in the lane; null with none.</summary>
    internal ScrollMark? MarkAt(Point point)
    {
        var lane = Lane();
        if (lane.Width <= 0 || point.X < lane.X || point.X > lane.Right)
        {
            return null;
        }
        ScrollMark? nearest = null;
        var best = Reach;
        foreach (var mark in Marks)
        {
            var distance = Math.Abs(YOf(mark, lane) + MarkHeight / 2 - point.Y);
            if (distance <= best)
            {
                (nearest, best) = (mark, distance);
            }
        }
        return nearest;
    }

    /// <summary>The bar's thumb is at <paramref name="point"/>: the pointer goes to it, to drag it, mark or no mark.</summary>
    private bool OnThumb(Point point) =>
        Bar?.GetVisualDescendants().OfType<Thumb>().FirstOrDefault() is { IsEffectivelyVisible: true } thumb
        && thumb.TranslatePoint(default, this) is { } at
        && new Rect(at, thumb.Bounds.Size).Contains(point);

    /// <summary>Only on a mark, off the thumb: anywhere else the pointer and the wheel reach the scroll bar and the conversation.</summary>
    public bool HitTest(Point point) => MarkAt(point) is not null && !OnThumb(point);

    public override void Render(DrawingContext context)
    {
        var lane = Lane();
        if (lane.Width <= 0)
        {
            return;
        }
        foreach (var mark in Marks)
        {
            context.FillRectangle(mark.Brush, new Rect(lane.X, YOf(mark, lane), lane.Width, MarkHeight), 1);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var mark = MarkAt(e.GetPosition(this));
        Cursor = mark is null ? null : Hand;
        ToolTip.SetTip(this, mark?.Tip);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && MarkAt(e.GetPosition(this)) is { } mark)
        {
            MarkClicked?.Invoke(mark);
            e.Handled = true;
        }
    }
}
