namespace Claudette.Core.ProjectTools.EditorLogs;

/// <summary>
/// The levels an editor's log is filtered by (DESIGN.md §18, "Editor logs"), most severe first. Unreal has them all,
/// with its Fatal as an error and its VeryVerbose as verbose; Unity's and Godot's lines are errors, warnings or
/// messages (<see cref="Log"/>).
/// </summary>
public enum LogVerbosity
{
    Error,
    Warning,
    Display,
    Log,
    Verbose,
}

/// <summary>A level an engine's lines have, and its name on the log's filter: "Errors", "Display", "Messages".</summary>
public sealed record EditorLogLevel(LogVerbosity Verbosity, string Name);

/// <summary>
/// A line of a log in the main area: its text as the file has it and, in an editor's log, what the engine's format says
/// of it (DESIGN.md §18, "Editor logs"). A run's lines are <see cref="Plain"/>.
/// </summary>
public sealed class LogLine(string text)
{
    /// <summary>The line as the file has it; what's copied and found.</summary>
    public string Text { get; } = text;

    /// <summary>Its entry's level, which it's filtered by and colored for.</summary>
    public LogVerbosity Verbosity { get; init; } = LogVerbosity.Log;

    /// <summary>
    /// It carries on the entry before it, such as a message's second line or a stack trace's: it's filtered with that
    /// one, and only the line that starts an entry is counted.
    /// </summary>
    public bool Continues { get; init; }

    /// <summary>A stack frame or the like, shown muted.</summary>
    public bool IsTrace { get; init; }

    /// <summary>Its entry's Unreal log category, "LogBlueprint"; null where the engine has none, or the line doesn't say.</summary>
    public string? Category { get; init; }

    /// <summary>Where its parts are, for Unreal's tidy columns; null when it isn't shown in them.</summary>
    public LogColumns? Columns { get; init; }

    /// <summary>How bad it is, for its color.</summary>
    public LogLineLevel Level => Verbosity switch
    {
        LogVerbosity.Error => LogLineLevel.Error,
        LogVerbosity.Warning => LogLineLevel.Warning,
        _ => LogLineLevel.Normal,
    };

    /// <summary>A line with nothing to say of it but its text, as a run's are.</summary>
    public static LogLine Plain(string text) => new(text);

    public override string ToString() => Text;
}

/// <summary>
/// Where an Unreal line's parts are (DESIGN.md §18, "Editor logs", "Its lines"), so it can be shown in columns, the
/// time, the category and the verbosity, with the file's text kept underneath:
/// <c>[2026.10.20-19.01.22:123][  0]LogBlueprint: Error: Can't connect pins</c>.
/// </summary>
/// <param name="PrefixLength">The time and frame in their brackets, <c>[2026.10.20-19.01.22:123][  0]</c>; 0 when it has none.</param>
/// <param name="Time">What's shown for them: <c>15:01:22.123</c> in the machine's time; null when it has none.</param>
/// <param name="CategoryLength">The category's, after the prefix; 0 when it has none.</param>
/// <param name="TagStart">Where its verbosity starts, <c>Error</c>; -1 when it has none, as a Log line has.</param>
/// <param name="TagLength">Its verbosity's.</param>
/// <param name="MessageStart">Where the message starts, after the <c>": "</c> that ends the category or the verbosity.</param>
public sealed record LogColumns(int PrefixLength, string? Time, int CategoryLength, int TagStart, int TagLength, int MessageStart)
{
    /// <summary>A line that carries on an entry shown in columns: it's indented to the message's.</summary>
    public static LogColumns CarriedOn { get; } = new(0, null, 0, -1, 0, 0);

    public bool IsCarriedOn => ReferenceEquals(this, CarriedOn);
}

/// <summary>
/// What's counted of a log's entries (DESIGN.md §18, "Editor logs"): how many there are at each level and in each
/// category, and whether one says its process crashed. A read's counts are what it adds to the log's.
/// </summary>
public sealed class LogCounts
{
    private readonly int[] _levels = new int[Enum.GetValues<LogVerbosity>().Length];

    public int this[LogVerbosity level] => _levels[(int)level];

    public int Errors => this[LogVerbosity.Error];

    public int Warnings => this[LogVerbosity.Warning];

    /// <summary>The entries in each of Unreal's categories.</summary>
    public Dictionary<string, int> Categories { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A line says its process crashed.</summary>
    public bool Crashed { get; internal set; }

    internal void Count(LogVerbosity level, int by = 1) => _levels[(int)level] += by;

    internal void CountCategory(string category) => Categories[category] = Categories.GetValueOrDefault(category) + 1;
}
