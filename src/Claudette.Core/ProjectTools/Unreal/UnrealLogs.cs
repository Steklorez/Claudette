using System.Globalization;
using System.Text.RegularExpressions;
using Claudette.Core.ProjectTools.EditorLogs;

namespace Claudette.Core.ProjectTools.Unreal;

/// <summary>
/// The logs Unreal's processes write for a project (DESIGN.md §18, "Editor logs"): the editor, a commandlet, or a game
/// or server run, each with the project's <c>.uproject</c> on its command line. Each writes
/// <c>Saved/Logs/&lt;Name&gt;.log</c>, or <c>&lt;Name&gt;_2.log</c> and on while another has that one open, unless
/// <c>-abslog=</c> or <c>LOG=</c> on its command line says otherwise. Starting again, Unreal renames the old log to
/// <c>&lt;Name&gt;-backup-&lt;date&gt;.log</c> and begins a new one, whose first line says when it opened.
/// </summary>
public static partial class UnrealLogs
{
    /// <summary>How many of <c>&lt;Name&gt;_2.log</c> on are looked at.</summary>
    private const int Numbered = 8;

    public static EditorLogSource Source(UnrealProject project, ToolOS os) => new()
    {
        ProcessNames = UnrealProvider.EditorProcessNames,
        Match = process => Writer(process, project.Path, project.Root, project.Name, os),
        Classify = Classify,
        IsCrash = IsCrash,
        OpenedAt = (file, zone) => OpenedAt(LogTail.FirstLine(file), zone),
        NewParser = Parser,
        Levels = Levels,
        HasCategories = true,
    };

    /// <summary>Unreal's verbosities, as its log is filtered by them: Fatal is an error, VeryVerbose verbose.</summary>
    public static IReadOnlyList<EditorLogLevel> Levels { get; } =
    [
        new(LogVerbosity.Error, "Errors"),
        new(LogVerbosity.Warning, "Warnings"),
        new(LogVerbosity.Display, "Display"),
        new(LogVerbosity.Log, "Log"),
        new(LogVerbosity.Verbose, "Verbose"),
    ];

    /// <summary>A reader for one of Unreal's logs, showing its times in <paramref name="zone"/>.</summary>
    public static EditorLogParser Parser(TimeZoneInfo zone) => new LineParser(zone);

    /// <summary>What <paramref name="process"/> is and the log it writes, when its command line names <paramref name="uproject"/>.</summary>
    public static EditorLogWriter? Writer(SystemProcess process, string uproject, string root, string name, ToolOS os)
    {
        if (!SystemProcessNames.CommandLineMentions(process, uproject))
        {
            return null;
        }
        var commandLine = process.CommandLine;
        var logs = os.Join(root, "Saved", "Logs");
        IReadOnlyList<string> files =
            CommandLineOptions.ValueOf(commandLine, "abslog") is { } absolute ? [absolute]
            : CommandLineOptions.ValueOf(commandLine, "log") is { } named ? [os.Join(logs, Path.HasExtension(named) ? named : $"{named}.log")]
            : [os.Join(logs, $"{name}.log"), .. Enumerable.Range(2, Numbered).Select(n => os.Join(logs, $"{name}_{n.ToString(CultureInfo.InvariantCulture)}.log"))];
        return new EditorLogWriter(Role(process), files);
    }

    /// <summary>"Unreal editor", "Unreal commandlet ResavePackages", "Unreal game", "Unreal server".</summary>
    public static string Role(SystemProcess process)
    {
        var commandLine = process.CommandLine;
        if (CommandLineOptions.ValueOf(commandLine, "run") is { } commandlet)
        {
            var bare = commandlet.EndsWith("Commandlet", StringComparison.OrdinalIgnoreCase) && commandlet.Length > "Commandlet".Length ? commandlet[..^"Commandlet".Length] : commandlet;
            return $"Unreal commandlet {bare}";
        }
        if (CommandLineOptions.Has(commandLine, "-game"))
        {
            return "Unreal game";
        }
        if (CommandLineOptions.Has(commandLine, "-server"))
        {
            return "Unreal server";
        }
        return process.Name.EndsWith("-Cmd", StringComparison.OrdinalIgnoreCase) ? "Unreal editor (command line)" : "Unreal editor";
    }

    /// <summary>
    /// <c>LogBlueprint: Error:</c> and <c>Warning:</c>, with or without the time and frame before them, and a
    /// compiler's <c>error C2065:</c> and <c>warning:</c>, as Live Coding writes them.
    /// </summary>
    public static LogLineLevel Classify(string line)
    {
        if (Category().Match(line) is { Success: true } match)
        {
            return match.Groups["level"].ValueSpan is "Warning" ? LogLineLevel.Warning : LogLineLevel.Error;
        }
        if (Compiler().Match(line) is { Success: true } compiler)
        {
            return compiler.Groups["level"].ValueSpan is "warning" ? LogLineLevel.Warning : LogLineLevel.Error;
        }
        return LogLineLevel.Normal;
    }

    /// <summary>Unreal's crash report in the log: <c>=== Critical error: ===</c>, or an assertion's <c>Fatal error: [File:</c>.</summary>
    public static bool IsCrash(string line) =>
        line.Contains("=== Critical error: ===", StringComparison.Ordinal) || line.Contains("Fatal error: [File:", StringComparison.Ordinal);

    /// <summary>
    /// When the log opened, from its first line, <c>Log file open, 10/20/26 15:01:22</c>, which is in the machine's
    /// time; null when it doesn't say.
    /// </summary>
    public static DateTimeOffset? OpenedAt(string? firstLine, TimeZoneInfo zone)
    {
        if (firstLine is null || Opened().Match(firstLine) is not { Success: true } match
            || !DateTime.TryParseExact(match.Groups[1].Value, "MM/dd/yy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>
    /// Reads Unreal's lines as entries (DESIGN.md §18, "Editor logs", "Its lines"):
    /// <c>[2026.10.20-19.01.22:123][  0]LogBlueprint: Error: message</c>, the time and the frame (the frame counter, to
    /// 1,000), then the category and the verbosity, which a Log line doesn't have; before the times start, as the editor
    /// starts, <c>LogInit: Display: message</c>. Once lines have times, one without carries on the one before, as a
    /// message's second line does, unless it has a category and a verbosity. The times are UTC unless the project has
    /// Unreal write its own, which the log's first line, in the machine's time, tells apart; or seconds since the start,
    /// <c>[0012.34]</c>.
    /// </summary>
    private sealed partial class LineParser(TimeZoneInfo zone) : EditorLogParser(Classify, IsCrash)
    {
        private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
        private DateTimeOffset? _opened;
        private bool? _utc;
        private bool _timed;
        private bool _inColumns;

        public override void Reset()
        {
            base.Reset();
            _opened = null;
            _utc = null;
            _timed = false;
            _inColumns = false;
        }

        protected override LineRead Read(string line, LogVerbosity? open)
        {
            if (line.StartsWith("Log file ", StringComparison.Ordinal))
            {
                // "Log file open, 10/20/26 15:01:22" and "Log file closed, …": a line of its own.
                _opened ??= OpenedAt(line, zone);
                _inColumns = false;
                return new LineRead(Starts: true);
            }
            var match = Parts().Match(line);
            var prefix = match.Groups["prefix"];
            var category = match.Groups["category"];
            var tag = match.Groups["tag"];
            // Without a time, once lines have them, only a category with its verbosity says a line is one of its own.
            if (!prefix.Success && open is not null && (_timed ? !(category.Success && tag.Success) : !category.Success))
            {
                return new LineRead(Starts: false, Columns: _inColumns ? LogColumns.CarriedOn : null);
            }
            if (!prefix.Success && !category.Success)
            {
                _inColumns = false;
                return new LineRead(Starts: true);
            }
            _timed |= prefix.Success;
            _inColumns = true;
            var (named, tagged) = (category.Success, tag.Success);
            var verbosity = tagged ? Verbosity(tag.ValueSpan) : null;
            if (named && !tagged && Verbosity(category.ValueSpan) is { } bare)
            {
                // "[…][  0]Warning: message", with the categories turned off: the verbosity is all there is.
                (named, tag, verbosity) = (false, category, bare);
                tagged = true;
            }
            var columns = new LogColumns(
                prefix.Length,
                prefix.Success ? TimeOf(match.Groups["time"].Value) : null,
                named ? category.Length : 0,
                tagged ? tag.Index : -1,
                tagged ? tag.Length : 0,
                match.Length);
            return new LineRead(Starts: true, verbosity ?? LogVerbosity.Log, named ? Name(category.ValueSpan) : null, columns);
        }

        private static LogVerbosity? Verbosity(ReadOnlySpan<char> tag) => tag switch
        {
            "Fatal" or "Error" => LogVerbosity.Error,
            "Warning" => LogVerbosity.Warning,
            "Display" => LogVerbosity.Display,
            "Log" => LogVerbosity.Log,
            "Verbose" or "VeryVerbose" => LogVerbosity.Verbose,
            _ => null,
        };

        /// <summary>"15:01:22.123" in the machine's time, "12.34s" since the start, or what the brackets have.</summary>
        private string TimeOf(string stamp)
        {
            if (DateTime.TryParseExact(stamp, "yyyy.MM.dd-HH.mm.ss:fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            {
                _utc ??= SeemsUtc(at);
                var local = _utc.Value ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(at, DateTimeKind.Utc), zone) : at;
                return local.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            }
            return double.TryParse(stamp, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                ? $"{seconds.ToString("0.00", CultureInfo.InvariantCulture)}s"
                : stamp.Trim();
        }

        /// <summary>UTC, unless read as the machine's time it's nearer when the log opened, which its first line says in that time.</summary>
        private bool SeemsUtc(DateTime at)
        {
            if (_opened is not { } opened)
            {
                return true;
            }
            var asLocal = (new DateTimeOffset(at, zone.GetUtcOffset(at)) - opened).Duration();
            var asUtc = (new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) - opened).Duration();
            return asUtc <= asLocal;
        }

        /// <summary>A category's name, one string for each, as there are tens of thousands of its lines.</summary>
        private string Name(ReadOnlySpan<char> name)
        {
            var lookup = _names.GetAlternateLookup<ReadOnlySpan<char>>();
            if (!lookup.TryGetValue(name, out var known))
            {
                known = name.ToString();
                _names[known] = known;
            }
            return known;
        }

        [GeneratedRegex(@"^(?<prefix>\[(?<time>[^\]]*)\]\[[^\]]*\])?(?:(?<category>[A-Za-z_]\w*): )?(?:(?<tag>Fatal|Error|Warning|Display|Log|Verbose|VeryVerbose): )?")]
        private static partial Regex Parts();
    }

    [GeneratedRegex(@"^(?:\[[^\]]*\])*\s*(?:[A-Za-z][\w]*:\s+)?(?<level>Error|Fatal error|Warning):")]
    private static partial Regex Category();

    [GeneratedRegex(@"[:)]\s*(?:fatal\s+)?(?<level>error|warning)(?:\s+[A-Z]+\d+)?:")]
    private static partial Regex Compiler();

    [GeneratedRegex(@"^Log file open, (\d\d/\d\d/\d\d \d\d:\d\d:\d\d)")]
    private static partial Regex Opened();
}
