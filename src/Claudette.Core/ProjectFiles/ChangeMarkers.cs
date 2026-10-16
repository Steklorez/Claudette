using Claudette.Core.Diffs;

namespace Claudette.Core.ProjectFiles;

/// <summary>How a line differs from the file's "before", for the gutter's bars.</summary>
public enum LineChange
{
    Added,
    Modified,
}

/// <summary>
/// One change, for the gutter's bar and its peek: the lines it covers now and the lines they replaced.
/// </summary>
/// <param name="FirstLine">The first line it covers now, 1-based; for lines only removed, the line they were above.</param>
/// <param name="LineCount">How many lines it covers now; 0 for lines only removed.</param>
/// <param name="OldLines">The lines it replaced, which <b>Revert</b> puts back.</param>
public sealed record ChangeBlock(int FirstLine, int LineCount, IReadOnlyList<string> OldLines)
{
    /// <summary>The last line it covers; for lines only removed, the line they were above.</summary>
    public int LastLine => LineCount == 0 ? FirstLine : FirstLine + LineCount - 1;

    public bool IsRemovalOnly => LineCount == 0;

    /// <summary>Whether <paramref name="line"/> is one of its lines, or the line its removal is marked on.</summary>
    public bool Covers(int line) => line >= FirstLine && line <= LastLine;
}

/// <summary>
/// The gutter's bars and peeks (DESIGN.md §18, "Project files", "Claude's changes"): a file's text compared with its
/// "before", from Core's line diff with no context. A changed line is one of a change's first lines while it removed
/// as many; past those, its lines are added. Lines only removed are marked on the line after them.
/// </summary>
public sealed class ChangeMarkers
{
    /// <summary>Files longer than this show no changes: comparing them on every pause in typing would be too slow.</summary>
    public const int MaxLines = 20_000;

    private readonly Dictionary<int, LineChange> _lines = [];
    private readonly HashSet<int> _removedAbove = [];

    private ChangeMarkers(IReadOnlyList<ChangeBlock> blocks)
    {
        Blocks = blocks;
        foreach (var block in blocks)
        {
            if (block.IsRemovalOnly)
            {
                _removedAbove.Add(block.FirstLine);
                continue;
            }
            for (var i = 0; i < block.LineCount; i++)
            {
                _lines[block.FirstLine + i] = i < block.OldLines.Count ? LineChange.Modified : LineChange.Added;
            }
        }
    }

    /// <summary>No changes.</summary>
    public static ChangeMarkers None { get; } = new([]);

    /// <summary>The changes, top to bottom.</summary>
    public IReadOnlyList<ChangeBlock> Blocks { get; }

    public bool IsEmpty => Blocks.Count == 0;

    public LineChange? KindOf(int line) => _lines.TryGetValue(line, out var kind) ? kind : null;

    /// <summary>Lines were removed just above <paramref name="line"/> (a line past the end: below the last).</summary>
    public bool HasRemovalAbove(int line) => _removedAbove.Contains(line);

    /// <summary>The change <paramref name="line"/> is in, or that's marked on it.</summary>
    public ChangeBlock? BlockAt(int line) => Blocks.FirstOrDefault(b => b.Covers(line));

    /// <summary>The first change below <paramref name="line"/>, going round to the first.</summary>
    public ChangeBlock? Next(int line) => Blocks.FirstOrDefault(b => b.FirstLine > line) ?? Blocks.FirstOrDefault();

    /// <summary>The last change above <paramref name="line"/>, going round to the last.</summary>
    public ChangeBlock? Previous(int line) => Blocks.LastOrDefault(b => b.LastLine < line) ?? Blocks.LastOrDefault();

    /// <summary>
    /// <paramref name="text"/> compared with <paramref name="before"/> (null: the file was new, so every line is added).
    /// Null when it's too long to compare, with <see cref="MaxLines"/>.
    /// </summary>
    public static ChangeMarkers? Compute(string? before, string text)
    {
        if (LineDiff.CountLines(text) > MaxLines || LineDiff.CountLines(before) > MaxLines)
        {
            return null;
        }
        var blocks = new List<ChangeBlock>();
        foreach (var hunk in LineDiff.Hunks(before, text, context: 0))
        {
            var removed = hunk.Lines.Where(l => l.Op == DiffOp.Removed).Select(l => l.Text).ToArray();
            var added = hunk.Lines.Where(l => l.Op == DiffOp.Added).ToArray();
            if (added.Length == 0)
            {
                // As in diff -u, a side with no lines starts at the line before the hunk.
                blocks.Add(new ChangeBlock(hunk.NewStart + 1, 0, removed));
            }
            else
            {
                blocks.Add(new ChangeBlock(added[0].NewNumber!.Value, added.Length, removed));
            }
        }
        return new ChangeMarkers(blocks);
    }
}
