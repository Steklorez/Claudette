namespace Claudette.App.Editor;

/// <summary>
/// An open file's text in the Project Files tab (DESIGN.md §18, "Project files"), with its undo history and whether it
/// has edits not saved yet. The app's is AvaloniaEdit's document (<see cref="TextEditorDocument"/>), which only the UI
/// thread may touch; view model tests, which have none, use a plain one.
/// </summary>
public interface IEditorDocument
{
    /// <summary>The whole text.</summary>
    string Text { get; }

    int LineCount { get; }

    /// <summary>The text of line <paramref name="line"/>, 1-based, without its line ending.</summary>
    string GetLineText(int line);

    /// <summary>Replaces the whole text, as one step Undo takes back: a file read again, or its line endings changed.</summary>
    void ReplaceAll(string text);

    /// <summary>
    /// Replaces <paramref name="count"/> lines from <paramref name="firstLine"/> (1-based) with <paramref name="lines"/>,
    /// each ending in <paramref name="newline"/>, as one step Undo takes back: a change reverted. With
    /// <paramref name="count"/> 0 the lines go in above <paramref name="firstLine"/> (a line past the end: after the last).
    /// </summary>
    void ReplaceLines(int firstLine, int count, IReadOnlyList<string> lines, string newline);

    /// <summary>Makes <paramref name="edits"/>, which don't overlap, as one step Undo takes back: Replace in files.</summary>
    void Replace(IReadOnlyList<Core.ProjectFiles.TextEdit> edits);

    /// <summary>It has edits since it was read or last saved; undoing back to then counts as none.</summary>
    bool IsModified { get; }

    /// <summary>Now is what's saved: it was read, or written to disk.</summary>
    void MarkSaved();

    bool CanUndo { get; }

    void Undo();

    /// <summary>The text changed.</summary>
    event EventHandler? Changed;

    /// <summary><see cref="IsModified"/> changed.</summary>
    event EventHandler? ModifiedChanged;
}
