namespace Claudette.Core.ProjectFiles;

/// <summary>How Tab and a new line indent: with tabs, or with so many spaces.</summary>
public sealed record IndentStyle(bool UseTabs, int Size)
{
    /// <summary>How the chip writes it: <c>4 spaces</c>, or <c>Tabs</c>.</summary>
    public string Label => UseTabs ? "Tabs" : Size == 1 ? "1 space" : $"{Size} spaces";

    /// <summary>What Tab types.</summary>
    public string Unit => UseTabs ? "\t" : new string(' ', Size);
}

/// <summary>
/// Finds how a file is indented (DESIGN.md §18, "Project files", "The chip"), so Tab and new lines indent as it does.
/// Lines starting with a tab outnumbering those starting with spaces means tabs; otherwise the step between one line's
/// indent and the next that's most common, of 2, 4 and 8, is the width.
/// </summary>
public static class Indentation
{
    /// <summary>The widths looked for, in the order a tie goes.</summary>
    private static readonly int[] Widths = [4, 2, 8];

    /// <summary>
    /// How <paramref name="lines"/> are indented, from their first <paramref name="maxLines"/>; null when none of them is.
    /// </summary>
    public static IndentStyle? Detect(IEnumerable<string> lines, int maxLines = 1000)
    {
        var tabs = 0;
        var spaced = 0;
        var steps = new Dictionary<int, int>();
        var previous = 0;
        var smallest = int.MaxValue;
        foreach (var line in lines.Take(maxLines))
        {
            var indent = 0;
            while (indent < line.Length && line[indent] is ' ' or '\t')
            {
                indent++;
            }
            if (indent == line.Length)
            {
                // Blank lines say nothing, and don't break a run.
                continue;
            }
            if (line[0] == '\t')
            {
                tabs++;
                continue;
            }
            if (indent > 0 && line.AsSpan(0, indent).IndexOf('\t') < 0)
            {
                spaced++;
                smallest = Math.Min(smallest, indent);
            }
            var step = Math.Abs(indent - previous);
            if (step > 1)
            {
                steps[step] = steps.GetValueOrDefault(step) + 1;
            }
            previous = line.AsSpan(0, indent).IndexOf('\t') < 0 ? indent : previous;
        }
        if (tabs == 0 && spaced == 0)
        {
            return null;
        }
        if (tabs > spaced)
        {
            return new IndentStyle(UseTabs: true, Size: 4);
        }
        var best = Widths.Select(w => (Width: w, Count: steps.GetValueOrDefault(w))).OrderByDescending(s => s.Count).First();
        if (best.Count > 0)
        {
            return new IndentStyle(UseTabs: false, Size: best.Width);
        }
        return new IndentStyle(UseTabs: false, Size: smallest is >= 2 and <= 8 ? smallest : 4);
    }
}
