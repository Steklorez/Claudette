using System.ComponentModel;
using AvaloniaEdit.Document;

namespace Claudette.App.Editor;

/// <summary>An open file's text as AvaloniaEdit's own document, which the editor shows and edits directly.</summary>
public sealed class TextEditorDocument : IEditorDocument
{
    public TextEditorDocument(string text)
    {
        Document = new TextDocument(text);
        // Reading the file isn't an edit to undo.
        Document.UndoStack.ClearAll();
        Document.UndoStack.MarkAsOriginalFile();
        Document.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        Document.UndoStack.PropertyChanged += OnUndoStackChanged;
    }

    /// <summary>The document the editor shows.</summary>
    public TextDocument Document { get; }

    public string Text => Document.Text;

    public int LineCount => Document.LineCount;

    public string GetLineText(int line) => line >= 1 && line <= Document.LineCount ? Document.GetText(Document.GetLineByNumber(line)) : "";

    public void ReplaceAll(string text) => Document.Replace(0, Document.TextLength, text);

    public void ReplaceLines(int firstLine, int count, IReadOnlyList<string> lines, string newline)
    {
        var (offset, length, text) = LineEdit.Plan(this, firstLine, count, lines, newline,
            line => (Document.GetLineByNumber(line).Offset, Document.GetLineByNumber(line).TotalLength, Document.GetLineByNumber(line).DelimiterLength), Document.TextLength);
        Document.Replace(offset, length, text);
    }

    public void Replace(IReadOnlyList<Core.ProjectFiles.TextEdit> edits)
    {
        if (edits.Count == 0)
        {
            return;
        }
        // One update is one step for Undo; from the end back, so each edit's offset still holds.
        using (Document.RunUpdate())
        {
            foreach (var edit in edits.OrderByDescending(e => e.Offset))
            {
                Document.Replace(edit.Offset, edit.Length, edit.Text);
            }
        }
    }

    public bool IsModified => !Document.UndoStack.IsOriginalFile;

    public void MarkSaved() => Document.UndoStack.MarkAsOriginalFile();

    public bool CanUndo => Document.UndoStack.CanUndo;

    public void Undo() => Document.UndoStack.Undo();

    public event EventHandler? Changed;

    public event EventHandler? ModifiedChanged;

    private void OnUndoStackChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UndoStack.IsOriginalFile))
        {
            ModifiedChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>How replacing whole lines maps onto the text, the same for every kind of document.</summary>
internal static class LineEdit
{
    /// <summary>
    /// Where to replace, and with what, to put <paramref name="lines"/> in place of <paramref name="count"/> lines from
    /// <paramref name="firstLine"/>. A file that ends without a newline still does; lines put in at the end of one that
    /// doesn't start on a new line.
    /// </summary>
    /// <param name="line">A line's offset, length with its ending, and its ending's length.</param>
    public static (int Offset, int Length, string Text) Plan(IEditorDocument document, int firstLine, int count, IReadOnlyList<string> lines, string newline,
        Func<int, (int Offset, int TotalLength, int DelimiterLength)> line, int textLength)
    {
        var lineCount = document.LineCount;
        var text = string.Concat(lines.Select(l => l + newline));
        if (firstLine > lineCount)
        {
            // After the last line: on a line of its own.
            var last = line(lineCount);
            var needsBreak = textLength > 0 && last.DelimiterLength == 0;
            return (textLength, 0, needsBreak && text.Length > 0 ? newline + text[..^newline.Length] : text);
        }
        var start = line(firstLine).Offset;
        if (count == 0)
        {
            return (start, 0, text);
        }
        var end = line(Math.Min(lineCount, firstLine + count - 1));
        var length = end.Offset + end.TotalLength - start;
        // The last line had no newline: neither does what takes its place.
        if (end.DelimiterLength == 0 && firstLine + count - 1 >= lineCount && text.Length > 0)
        {
            text = text[..^newline.Length];
        }
        return (start, length, text);
    }
}
