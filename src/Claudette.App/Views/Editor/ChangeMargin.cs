using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Claudette.Core.ProjectFiles;

namespace Claudette.App.Views.Editor;

/// <summary>
/// The gutter's bars (DESIGN.md §18, "Project files", "Claude's changes"), between the line numbers and the text: green
/// beside lines added, the accent beside lines changed, and a red notch between two lines where lines were removed. A
/// click on one opens or closes its change's peek.
/// </summary>
internal sealed class ChangeMargin : AbstractMargin
{
    private const double BarWidth = 3;

    private ChangeMarkers _markers = ChangeMarkers.None;

    public ChangeMargin()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>A click on a bar: the line it's on.</summary>
    public event Action<int>? Clicked;

    public ChangeMarkers Markers
    {
        get => _markers;
        set
        {
            _markers = value;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(BarWidth + 4, 0);

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null)
        {
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        }
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null)
        {
            newTextView.VisualLinesChanged += OnVisualLinesChanged;
        }
        InvalidateVisual();
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        // A transparent face, so a click anywhere down the margin reaches it.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var view = TextView;
        if (view is null || !view.VisualLinesValid || _markers.IsEmpty)
        {
            return;
        }
        var added = Brush("ChangeAddedBrush", Colors.SeaGreen);
        var modified = Brush("ChangeModifiedBrush", Colors.DodgerBlue);
        var removed = Brush("ChangeRemovedBrush", Colors.IndianRed);
        var height = view.DefaultLineHeight;
        foreach (var line in view.VisualLines)
        {
            var number = line.FirstDocumentLine.LineNumber;
            var top = line.VisualTop - view.ScrollOffset.Y;
            if (_markers.KindOf(number) is { } kind)
            {
                context.FillRectangle(kind == LineChange.Added ? added : modified, new Rect(2, top, BarWidth, height));
            }
            if (_markers.HasRemovalAbove(number))
            {
                Notch(context, removed, top);
            }
            // Lines removed at the very end are marked under the last.
            if (number == view.Document.LineCount && _markers.HasRemovalAbove(number + 1))
            {
                Notch(context, removed, top + height);
            }
        }
    }

    private static void Notch(DrawingContext context, IBrush brush, double y)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(1, y - 4), true);
            g.LineTo(new Point(BarWidth + 4, y));
            g.LineTo(new Point(1, y + 4));
            g.EndFigure(true);
        }
        context.DrawGeometry(brush, null, geometry);
    }

    private IBrush Brush(string key, Color fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : new SolidColorBrush(fallback);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var view = TextView;
        if (view is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        var y = e.GetPosition(this).Y + view.ScrollOffset.Y;
        if (view.GetVisualLineFromVisualTop(y) is { } line)
        {
            var number = line.FirstDocumentLine.LineNumber;
            // A notch sits on the line under it, half above: a click just over a line counts for it too.
            if (_markers.BlockAt(number) is null && _markers.BlockAt(number + 1) is { IsRemovalOnly: true } && y > line.VisualTop + view.DefaultLineHeight * 0.66)
            {
                number++;
            }
            if (_markers.BlockAt(number) is not null)
            {
                Clicked?.Invoke(number);
                e.Handled = true;
            }
        }
    }
}
