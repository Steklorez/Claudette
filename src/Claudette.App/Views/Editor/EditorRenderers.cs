using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;

namespace Claudette.App.Views.Editor;

/// <summary>
/// The current line, drawn faintly behind its text, the text's height rather than its row's: AvaloniaEdit's own covers a
/// peek opened under the line too (DESIGN.md §18, "Project files", "Spike findings").
/// </summary>
internal sealed class CurrentLineHighlight(TextArea area, Func<IBrush?> brush) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (brush() is not { } fill || area.Selection.Length > 0 || !textView.VisualLinesValid)
        {
            return;
        }
        var line = textView.VisualLines.FirstOrDefault(l => l.FirstDocumentLine.LineNumber <= area.Caret.Line && l.LastDocumentLine.LineNumber >= area.Caret.Line);
        if (line is null)
        {
            return;
        }
        var top = line.VisualTop - textView.ScrollOffset.Y;
        drawingContext.FillRectangle(fill, new Rect(0, top, textView.Bounds.Width, textView.DefaultLineHeight));
    }
}

/// <summary>
/// The caret on a line with a peek under it: AvaloniaEdit draws its caret as tall as the line's row, which would run
/// down the peek, so there its own is made transparent and this draws one the text's height (DESIGN.md §18, "Project
/// files", "Spike findings"). Elsewhere AvaloniaEdit's own caret shows, blinking.
/// </summary>
internal sealed class PeekCaretLayer : Control
{
    private readonly TextArea _area;
    private readonly Func<int?> _peekLine;

    public PeekCaretLayer(TextArea area, Func<int?> peekLine)
    {
        _area = area;
        _peekLine = peekLine;
        IsHitTestVisible = false;
    }

    /// <summary>The caret is on the line the peek is under, so this one draws it.</summary>
    public bool IsActive => _peekLine() is { } line && line == _area.Caret.Line;

    public override void Render(DrawingContext context)
    {
        if (!IsActive || !_area.IsFocused || _area.TextView is not { VisualLinesValid: true } view)
        {
            return;
        }
        var rect = _area.Caret.CalculateCaretRectangle();
        var brush = _area.TextView.GetValue(TextBlock.ForegroundProperty) ?? Brushes.Black;
        context.FillRectangle(brush, new Rect(rect.X - view.ScrollOffset.X, rect.Y - view.ScrollOffset.Y, Math.Max(1.5, rect.Width), view.DefaultLineHeight));
    }
}

/// <summary>
/// Folding by indentation, in any language (DESIGN.md §18, "Project files", "The editor"): a line whose next line is
/// indented further starts a fold, which runs to the last line indented further than it. AvaloniaEdit only comes with
/// XML's.
/// </summary>
internal static class IndentationFolding
{
    private const int TabWidth = 4;

    /// <summary>The folds of <paramref name="document"/>, by start offset.</summary>
    public static List<NewFolding> Compute(TextDocument document)
    {
        var lines = new List<(int Number, int Indent, int EndOffset)>(document.LineCount);
        foreach (var line in document.Lines)
        {
            var indent = 0;
            var blank = true;
            for (var offset = line.Offset; offset < line.EndOffset; offset++)
            {
                var c = document.GetCharAt(offset);
                if (c == ' ')
                {
                    indent++;
                }
                else if (c == '\t')
                {
                    indent += TabWidth - indent % TabWidth;
                }
                else
                {
                    blank = false;
                    break;
                }
            }
            if (!blank)
            {
                lines.Add((line.LineNumber, indent, line.EndOffset));
            }
        }
        var folds = new List<NewFolding>();
        var open = new Stack<(int Number, int Indent, int EndOffset)>();
        for (var k = 0; k < lines.Count; k++)
        {
            var current = lines[k];
            while (open.Count > 0 && open.Peek().Indent >= current.Indent)
            {
                Close(open.Pop(), lines[k - 1]);
            }
            if (k + 1 < lines.Count && lines[k + 1].Indent > current.Indent)
            {
                open.Push(current);
            }
        }
        while (open.Count > 0 && lines.Count > 0)
        {
            Close(open.Pop(), lines[^1]);
        }
        folds.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return folds;

        void Close((int Number, int Indent, int EndOffset) start, (int Number, int Indent, int EndOffset) last)
        {
            if (last.Number > start.Number)
            {
                folds.Add(new NewFolding(start.EndOffset, last.EndOffset) { Name = "…" });
            }
        }
    }
}
