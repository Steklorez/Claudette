using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Claudette.App.Views.Editor;

/// <summary>
/// Opens room for the peek under a line (DESIGN.md §18, "Project files", "Spike findings"): an inline object at the
/// line's end, whose baseline is the text's, so the line's row grows downward by the peek's height and the lines under it
/// move down. It's placed by a <see cref="TextAnchor"/>, so it moves with the text as lines are added above.
/// </summary>
internal sealed class ChangePeekGenerator : VisualLineElementGenerator
{
    private readonly TextView _view;
    private readonly PeekHost _host;
    private TextAnchor? _anchor;

    public ChangePeekGenerator(TextView view, Control content)
    {
        _view = view;
        _host = new PeekHost(view, content);
    }

    /// <summary>The line the peek is under now, or null while it's closed.</summary>
    public int? Line => _anchor is { IsDeleted: false } anchor ? anchor.Line : null;

    /// <summary>Puts the peek under <paramref name="line"/> of <paramref name="document"/>.</summary>
    public void Place(TextDocument document, int line)
    {
        line = Math.Clamp(line, 1, document.LineCount);
        _anchor = document.CreateAnchor(document.GetLineByNumber(line).Offset);
        _anchor.SurviveDeletion = true;
        if (!_view.ElementGenerators.Contains(this))
        {
            _view.ElementGenerators.Add(this);
        }
        _host.InvalidateMeasure();
        _view.Redraw();
    }

    /// <summary>Closes the peek: the lines under it move back up.</summary>
    public void Remove()
    {
        _anchor = null;
        if (_view.ElementGenerators.Remove(this))
        {
            _view.Redraw();
        }
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        var line = CurrentContext.VisualLine.FirstDocumentLine;
        if (Line != line.LineNumber)
        {
            return -1;
        }
        return line.EndOffset >= startOffset ? line.EndOffset : -1;
    }

    public override VisualLineElement ConstructElement(int offset) => new InlineObjectElement(0, _host);

    /// <summary>
    /// The peek's room: one pixel wide, so the line doesn't scroll further sideways, and the text's baseline, so the row
    /// grows downward. Its content is drawn from the text view's left edge, as wide as the view, under the line's text.
    /// </summary>
    private sealed class PeekHost : Decorator
    {
        private readonly TextView _view;

        public PeekHost(TextView view, Control content)
        {
            _view = view;
            Child = content;
            ClipToBounds = false;
            LayoutUpdated += (_, _) =>
            {
                // Drawn from the text view's left edge, wherever the line's text ends.
                var x = -Bounds.X;
                if (RenderTransform is not TranslateTransform { } moved || Math.Abs(moved.X - x) > 0.5)
                {
                    RenderTransform = new TranslateTransform(x, 0);
                }
            };
        }

        private double ContentWidth => Math.Max(200, _view.Bounds.Width - 2);

        protected override Size MeasureOverride(Size availableSize)
        {
            TextBlock.SetBaselineOffset(this, _view.DefaultBaseline);
            Child!.Measure(new Size(ContentWidth, double.PositiveInfinity));
            return new Size(1, _view.DefaultLineHeight + Child.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Child!.Arrange(new Rect(0, _view.DefaultLineHeight, ContentWidth, Child.DesiredSize.Height));
            return finalSize;
        }
    }
}
