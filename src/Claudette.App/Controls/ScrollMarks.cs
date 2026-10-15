using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;

namespace Claudette.App.Controls;

/// <summary>A mark beside the scroll bar: where its item is, as a part of the conversation's height, and what it is.</summary>
/// <param name="Index">The item's place in the conversation.</param>
public sealed record ScrollMark(double Fraction, IBrush Brush, int Index, string Tip);

/// <summary>
/// Marks beside the conversation's scroll bar where the user's messages, waiting prompts, errors and changed files are
/// (DESIGN.md §18, "Conversation cards"). A click on one goes there. Only the marks take the pointer, so the conversation
/// under the strip still scrolls and selects.
/// </summary>
public sealed class ScrollMarks : Control, ICustomHitTest
{
    public static readonly StyledProperty<IReadOnlyList<ScrollMark>> MarksProperty =
        AvaloniaProperty.Register<ScrollMarks, IReadOnlyList<ScrollMark>>(nameof(Marks), []);

    /// <summary>A mark's height; marks closer than this draw over each other.</summary>
    private const double MarkHeight = 3;

    /// <summary>How far from a mark the pointer still points at it.</summary>
    private const double Reach = 4;

    private static readonly Cursor Hand = new(StandardCursorType.Hand);

    static ScrollMarks() => AffectsRender<ScrollMarks>(MarksProperty);

    public IReadOnlyList<ScrollMark> Marks
    {
        get => GetValue(MarksProperty);
        set => SetValue(MarksProperty, value);
    }

    /// <summary>A mark was clicked: the conversation goes to its item.</summary>
    public event Action<ScrollMark>? MarkClicked;

    private double YOf(ScrollMark mark) => Math.Clamp(mark.Fraction * Bounds.Height, 0, Math.Max(0, Bounds.Height - MarkHeight));

    /// <summary>The mark nearest <paramref name="y"/>, within reach; null with none.</summary>
    internal ScrollMark? MarkAt(double y)
    {
        ScrollMark? nearest = null;
        var best = Reach;
        foreach (var mark in Marks)
        {
            var distance = Math.Abs(YOf(mark) + MarkHeight / 2 - y);
            if (distance <= best)
            {
                (nearest, best) = (mark, distance);
            }
        }
        return nearest;
    }

    /// <summary>Only on a mark, inside the strip: anywhere else the pointer, and the wheel, reach the conversation under it.</summary>
    public bool HitTest(Point point) => point.X >= 0 && point.X <= Bounds.Width && MarkAt(point.Y) is not null;

    public override void Render(DrawingContext context)
    {
        foreach (var mark in Marks)
        {
            context.FillRectangle(mark.Brush, new Rect(0, YOf(mark), Bounds.Width, MarkHeight), 1);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var mark = MarkAt(e.GetPosition(this).Y);
        Cursor = mark is null ? null : Hand;
        ToolTip.SetTip(this, mark?.Tip);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && MarkAt(e.GetPosition(this).Y) is { } mark)
        {
            MarkClicked?.Invoke(mark);
            e.Handled = true;
        }
    }
}
