namespace Claudette.Core.ProjectTools.EditorLogs;

/// <summary>
/// Reads an engine's log a line at a time as its entries (DESIGN.md §18, "Editor logs", "Its lines"): a line that starts
/// one, with its level and category, and the lines that carry it on, such as a message's second line or a stack trace,
/// which are filtered with it. Only an entry is counted. Each log has its own, fed its lines in order, since what a line
/// is depends on the lines before; <see cref="Reset"/> when the log starts afresh.
/// </summary>
/// <param name="classify">The engine's markers for an error or a warning, which make a line one whatever else it says.</param>
/// <param name="isCrash">Whether a line says the process crashed.</param>
public abstract class EditorLogParser(Func<string, LogLineLevel> classify, Func<string, bool> isCrash)
{
    private readonly List<(string Text, Entry Entry, LineRead Read)> _kept = [];
    private LogCounts _counts = new();
    private Entry? _open;

    /// <summary>
    /// The next of the log's lines. One <paramref name="kept"/> false is only counted: it's before the part of a big
    /// file that's read.
    /// </summary>
    public void Add(string line, bool kept = true)
    {
        if (isCrash(line))
        {
            _counts.Crashed = true;
        }
        var read = Read(line, _open?.Verbosity);
        var marked = classify(line) switch
        {
            LogLineLevel.Error => LogVerbosity.Error,
            LogLineLevel.Warning => LogVerbosity.Warning,
            _ => (LogVerbosity?)null,
        };
        if (!read.Starts && _open is { } carried && marked is { } own && own < carried.Verbosity)
        {
            // An error or a warning of its own within another entry: an entry of its own, where it is.
            read = read with { Starts = true, Verbosity = own, Category = carried.Category, IsTrace = false };
        }
        if (read.Starts || _open is null)
        {
            var verbosity = marked is { } worse && worse < read.Verbosity ? worse : read.Verbosity;
            _open = new Entry(verbosity, read.Category);
            _counts.Count(verbosity);
            if (read.Category is { } category)
            {
                _counts.CountCategory(category);
            }
            read = read with { Starts = true };
        }
        else if (read.Raises is { } raised && raised < _open.Verbosity && !_open.Shown)
        {
            // Unity says what an entry was in its stack trace, after its message.
            _counts.Count(_open.Verbosity, -1);
            _counts.Count(raised);
            _open.Verbosity = raised;
        }
        if (kept)
        {
            _kept.Add((line, _open, read));
        }
    }

    /// <summary>
    /// The lines kept since the last take, as the log shows them. An entry's level is settled once one of its lines is
    /// taken: a later line can't change what's shown.
    /// </summary>
    public IReadOnlyList<LogLine> TakeLines()
    {
        var lines = new LogLine[_kept.Count];
        for (var i = 0; i < lines.Length; i++)
        {
            var (text, entry, read) = _kept[i];
            lines[i] = new LogLine(text)
            {
                Verbosity = entry.Verbosity,
                Continues = !read.Starts,
                IsTrace = read.IsTrace,
                Category = entry.Category,
                Columns = read.Columns,
            };
            entry.Shown = true;
        }
        _kept.Clear();
        return lines;
    }

    /// <summary>What's been counted since the last take.</summary>
    public LogCounts TakeCounts()
    {
        var counts = _counts;
        _counts = new LogCounts();
        return counts;
    }

    /// <summary>The log starts afresh, as when the editor starts again: nothing before it counts.</summary>
    public virtual void Reset()
    {
        _kept.Clear();
        _counts = new LogCounts();
        _open = null;
    }

    /// <summary>What the engine's format says of a line, after <paramref name="open"/>'s entry, the level of the entry it would carry on.</summary>
    protected abstract LineRead Read(string line, LogVerbosity? open);

    /// <summary>What a line is.</summary>
    /// <param name="Starts">It starts an entry, at <paramref name="Verbosity"/> and in <paramref name="Category"/>; otherwise it carries on the one before.</param>
    /// <param name="Raises">A line carrying an entry on that says the entry is worse: Unity's <c>UnityEngine.Debug:LogWarning</c>.</param>
    protected readonly record struct LineRead(
        bool Starts,
        LogVerbosity Verbosity = LogVerbosity.Log,
        string? Category = null,
        LogColumns? Columns = null,
        bool IsTrace = false,
        LogVerbosity? Raises = null);

    private sealed class Entry(LogVerbosity verbosity, string? category)
    {
        public LogVerbosity Verbosity { get; set; } = verbosity;

        public string? Category { get; } = category;

        /// <summary>One of its lines has been taken, so its level is what's shown.</summary>
        public bool Shown { get; set; }
    }
}
