using AvaloniaEdit.Document;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using TextMateSharp.Model;

namespace Claudette.App.Views.Editor;

/// <summary>
/// An open file's lines as TextMateSharp's background tokenizer reads them (DESIGN.md §18, "Project files", "Spike
/// findings"). It follows AvaloniaEdit.TextMate's own <c>TextEditorModel</c> (MIT), with one difference: that one
/// tokenizes every line on screen again, on the UI thread, each time the view scrolls, which froze the editor for
/// seconds on a long minified line. Here only the background tokenizer does, as edits invalidate lines.
/// </summary>
internal sealed class TextMateLines : AbstractLineList
{
    private readonly TextDocument _document;
    private readonly DocumentSnapshot _snapshot;
    private (int Start, int End)? _invalidDuringUpdate;

    public TextMateLines(TextDocument document)
    {
        _document = document;
        _snapshot = new DocumentSnapshot(document);
        for (var i = 0; i < document.LineCount; i++)
        {
            AddLine(i);
        }
        document.Changing += OnChanging;
        document.Changed += OnChanged;
        document.UpdateFinished += OnUpdateFinished;
    }

    public override void Dispose()
    {
        _document.Changing -= OnChanging;
        _document.Changed -= OnChanged;
        _document.UpdateFinished -= OnUpdateFinished;
    }

    public override void UpdateLine(int lineIndex)
    {
    }

    public override int GetNumberOfLines() => _snapshot.LineCount;

    public override LineText GetLineTextIncludingTerminators(int lineIndex) => _snapshot.GetLineTextIncludingTerminatorAsMemory(lineIndex);

    public override int GetLineLength(int lineIndex) => _snapshot.GetLineLength(lineIndex);

    private void OnChanging(object? sender, DocumentChangeEventArgs e)
    {
        if (e.RemovalLength <= 0)
        {
            return;
        }
        var start = _document.GetLineByOffset(e.Offset).LineNumber - 1;
        var end = _document.GetLineByOffset(e.Offset + e.RemovalLength).LineNumber - 1;
        for (var i = end; i > start; i--)
        {
            RemoveLine(i);
        }
        _snapshot.RemoveLines(start, end);
    }

    private void OnChanged(object? sender, DocumentChangeEventArgs e)
    {
        var start = _document.GetLineByOffset(e.Offset).LineNumber - 1;
        var end = start;
        if (e.InsertionLength > 0)
        {
            end = _document.GetLineByOffset(e.Offset + e.InsertionLength).LineNumber - 1;
            for (var i = start; i < end; i++)
            {
                AddLine(i);
            }
        }
        _snapshot.Update(e);
        // Some grammars (JSON, C#) need the line before as well.
        Invalidate(Math.Max(0, start - 1), end);
    }

    private void Invalidate(int start, int end)
    {
        if (!_document.IsInUpdate)
        {
            InvalidateLineRange(start, end);
            return;
        }
        // Within one edit made of several changes: once, when it's done.
        _invalidDuringUpdate = _invalidDuringUpdate is { } range ? (Math.Min(range.Start, start), Math.Max(range.End, end)) : (start, end);
    }

    private void OnUpdateFinished(object? sender, EventArgs e)
    {
        if (_invalidDuringUpdate is not { } range)
        {
            return;
        }
        _invalidDuringUpdate = null;
        var last = Math.Max(0, _snapshot.LineCount - 1);
        InvalidateLineRange(Math.Clamp(range.Start, 0, last), Math.Clamp(range.End, 0, last));
    }
}
