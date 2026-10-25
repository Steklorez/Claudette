namespace Claudette.App.Editor;

/// <summary>
/// Scrolling a Markdown file's editor and preview together (DESIGN.md §18, "Markdown preview", "Scrolling together"): a
/// place in the preview for each line of the file, and the line for each place. It goes by the preview's blocks of text
/// (each paragraph, heading, list item, table cell and code block), which know the lines they came from; within a block,
/// and between two, the place goes in proportion. Lines are 1-based and fractional: 3.5 is halfway down line 3.
/// </summary>
public sealed class PreviewScroll
{
    /// <summary>A block of the preview's text: the file's lines it came from, and where it is in the preview.</summary>
    public readonly record struct Block(int FirstLine, int LastLine, double Top, double Height);

    public static PreviewScroll None { get; } = new([]);

    /// <summary>One block a line, going down both the file and the preview.</summary>
    private readonly Block[] _blocks;

    /// <summary>
    /// The map from the preview's <paramref name="blocks"/>. Only those that go down the file and the preview together
    /// count: a table's cells after the first in a row, and anything laid out above a block from earlier in the file, are
    /// left out.
    /// </summary>
    public PreviewScroll(IEnumerable<Block> blocks)
    {
        var kept = new List<Block>();
        foreach (var block in blocks.OrderBy(b => b.FirstLine).ThenBy(b => b.Top))
        {
            if (block.LastLine < block.FirstLine || block.Height < 0)
            {
                continue;
            }
            if (kept.Count > 0 && (block.FirstLine <= kept[^1].LastLine || block.Top < kept[^1].Top))
            {
                continue;
            }
            kept.Add(block);
        }
        _blocks = [.. kept];
    }

    public int Count => _blocks.Length;

    /// <summary>How far down the preview <paramref name="line"/> is.</summary>
    public double OffsetFor(double line)
    {
        if (_blocks.Length == 0)
        {
            return 0;
        }
        var i = LastWhere(b => b.FirstLine <= line);
        if (i < 0)
        {
            // Above the first block: from the top down to it.
            var first = _blocks[0];
            return first.FirstLine <= 1 ? 0 : first.Top * Math.Clamp((line - 1) / (first.FirstLine - 1), 0, 1);
        }
        var block = _blocks[i];
        var end = block.LastLine + 1;
        if (line < end)
        {
            return block.Top + (line - block.FirstLine) / (end - block.FirstLine) * block.Height;
        }
        var bottom = block.Top + block.Height;
        if (i + 1 == _blocks.Length)
        {
            return bottom;
        }
        // Between this block and the next: blank lines, or what the preview doesn't show.
        var next = _blocks[i + 1];
        return bottom + (line - end) / (next.FirstLine - end) * Math.Max(0, next.Top - bottom);
    }

    /// <summary>The line at <paramref name="offset"/> down the preview.</summary>
    public double LineFor(double offset)
    {
        if (_blocks.Length == 0)
        {
            return 1;
        }
        var i = LastWhere(b => b.Top <= offset);
        if (i < 0)
        {
            var first = _blocks[0];
            return first.Top <= 0 ? first.FirstLine : 1 + (first.FirstLine - 1) * Math.Clamp(offset / first.Top, 0, 1);
        }
        var block = _blocks[i];
        var end = block.LastLine + 1;
        var bottom = block.Top + block.Height;
        if (offset < bottom)
        {
            return block.FirstLine + (offset - block.Top) / block.Height * (end - block.FirstLine);
        }
        if (i + 1 == _blocks.Length)
        {
            return end;
        }
        var next = _blocks[i + 1];
        return next.Top <= bottom ? next.FirstLine : end + Math.Min(1, (offset - bottom) / (next.Top - bottom)) * (next.FirstLine - end);
    }

    /// <summary>The last block that <paramref name="before"/> holds for, which goes from all of them to none down the list.</summary>
    private int LastWhere(Func<Block, bool> before)
    {
        var low = 0;
        var high = _blocks.Length - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (before(_blocks[middle]))
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return found;
    }
}
