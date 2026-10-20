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
    };

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

    [GeneratedRegex(@"^(?:\[[^\]]*\])*\s*(?:[A-Za-z][\w]*:\s+)?(?<level>Error|Fatal error|Warning):")]
    private static partial Regex Category();

    [GeneratedRegex(@"[:)]\s*(?:fatal\s+)?(?<level>error|warning)(?:\s+[A-Z]+\d+)?:")]
    private static partial Regex Compiler();

    [GeneratedRegex(@"^Log file open, (\d\d/\d\d/\d\d \d\d:\d\d:\d\d)")]
    private static partial Regex Opened();
}
