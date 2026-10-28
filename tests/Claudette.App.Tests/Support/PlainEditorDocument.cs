using Claudette.App.Editor;

namespace Claudette.App.Tests.Support;

/// <summary>
/// An open file's text for view model tests (DESIGN.md §18, "Project files"): AvaloniaEdit's document only answers the
/// thread that made it, and these tests have no UI thread. Undo goes back a whole edit at a time.
/// </summary>
internal sealed class PlainEditorDocument(string text) : IEditorDocument
{
    private readonly Stack<string> _undo = new();
    private string _saved = text;

    public string Text { get; private set; } = text;

    public int LineCount => Lines().Length;

    public string GetLineText(int line) => line >= 1 && line <= LineCount ? Lines()[line - 1] : "";

    public void ReplaceAll(string text) => Change(text);

    public void ReplaceLines(int firstLine, int count, IReadOnlyList<string> lines, string newline)
    {
        // The same plan as the app's document, over the text's own line offsets.
        var offsets = LineOffsets();
        var (offset, length, replacement) = LineEdit.Plan(this, firstLine, count, lines, newline, line => offsets[line - 1], Text.Length);
        Change(Text[..offset] + replacement + Text[(offset + length)..]);
    }

    public void Replace(IReadOnlyList<Core.ProjectFiles.TextEdit> edits) => Change(Core.ProjectFiles.FindReplace.Apply(Text, edits));

    /// <summary>Types <paramref name="typed"/> at <paramref name="offset"/>, as a user would.</summary>
    public void Insert(int offset, string typed) => Change(Text[..offset] + typed + Text[offset..]);

    public bool IsModified => !string.Equals(Text, _saved, StringComparison.Ordinal);

    public void MarkSaved()
    {
        var was = IsModified;
        _saved = Text;
        if (was)
        {
            ModifiedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool CanUndo => _undo.Count > 0;

    public void Undo()
    {
        if (_undo.Count > 0)
        {
            Set(_undo.Pop());
        }
    }

    public event EventHandler? Changed;

    public event EventHandler? ModifiedChanged;

    private void Change(string text)
    {
        _undo.Push(Text);
        Set(text);
    }

    private void Set(string text)
    {
        var was = IsModified;
        Text = text;
        Changed?.Invoke(this, EventArgs.Empty);
        if (was != IsModified)
        {
            ModifiedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The lines, as AvaloniaEdit counts them: a final newline starts an empty last line.</summary>
    private string[] Lines() => Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>Each line's offset, length with its ending, and its ending's length.</summary>
    private List<(int Offset, int TotalLength, int DelimiterLength)> LineOffsets()
    {
        var result = new List<(int, int, int)>();
        var start = 0;
        for (var i = 0; i < Text.Length; i++)
        {
            if (Text[i] == '\n' || Text[i] == '\r')
            {
                var delimiter = Text[i] == '\r' && i + 1 < Text.Length && Text[i + 1] == '\n' ? 2 : 1;
                result.Add((start, i - start + delimiter, delimiter));
                i += delimiter - 1;
                start = i + 1;
            }
        }
        // The last line, empty after a final newline, as AvaloniaEdit has it.
        result.Add((start, Text.Length - start, 0));
        return result;
    }
}
