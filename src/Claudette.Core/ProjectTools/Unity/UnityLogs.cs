using System.Text.RegularExpressions;
using Claudette.Core.ProjectTools.EditorLogs;

namespace Claudette.Core.ProjectTools.Unity;

/// <summary>
/// The logs Unity writes for a project (DESIGN.md §18, "Editor logs"): the editor or a batch mode run with the project's
/// folder after <c>-projectPath</c>. Each writes the machine's <c>Editor.log</c>, which Unity moves to
/// <c>Editor-prev.log</c> as it starts, unless <c>-logFile</c> names another file; <c>-logFile -</c> writes none.
/// </summary>
public static partial class UnityLogs
{
    public static EditorLogSource Source(UnityProject project, ProjectToolPaths paths, ToolOS os) => new()
    {
        ProcessNames = UnityProvider.EditorProcessNames,
        Match = process => Writer(process, project.Root, UnityEditors.EditorLog(paths, os), os),
        Classify = Classify,
        IsCrash = IsCrash,
        NewParser = _ => Parser(),
    };

    /// <summary>A reader for one of Unity's logs.</summary>
    public static EditorLogParser Parser() => new LineParser();

    /// <summary>What <paramref name="process"/> is and the log it writes, when it has <paramref name="root"/> open.</summary>
    public static EditorLogWriter? Writer(SystemProcess process, string root, string? editorLog, ToolOS os)
    {
        var commandLine = process.CommandLine;
        if (CommandLineOptions.ValueAfter(commandLine, "-projectPath") is not { } opened || !SamePath(opened, root, os))
        {
            return null;
        }
        string? log = editorLog;
        if (CommandLineOptions.Has(commandLine, "-logFile"))
        {
            var named = CommandLineOptions.ValueAfter(commandLine, "-logFile");
            if (named == "-")
            {
                // To standard output: no file.
                return null;
            }
            if (named is { Length: > 0 } && !named.StartsWith('-'))
            {
                log = os.IsRooted(named) ? named : os.Join(root, named);
            }
        }
        return log is null ? null : new EditorLogWriter(CommandLineOptions.Has(commandLine, "-batchmode") ? "Unity (batch mode)" : "Unity editor", [log]);
    }

    /// <summary>The C# compiler's <c>error CS0103</c> and <c>warning CS0168</c>, exceptions, and lines that start with Error or Warning.</summary>
    public static LogLineLevel Classify(string line) =>
        ErrorLine().IsMatch(line) ? LogLineLevel.Error
        : WarningLine().IsMatch(line) ? LogLineLevel.Warning
        : LogLineLevel.Normal;

    /// <summary>Unity's crash report in the log: <c>Crash!!!</c>, or <c>Native Crash Reporting</c>.</summary>
    public static bool IsCrash(string line) =>
        line.Contains("Crash!!!", StringComparison.Ordinal) || line.Contains("Native Crash Reporting", StringComparison.Ordinal);

    private static bool SamePath(string a, string b, ToolOS os) =>
        string.Equals(Normal(a), Normal(b), os.PathComparison());

    private static string Normal(string path) => path.Replace('\\', '/').TrimEnd('/');

    /// <summary>
    /// Reads Unity's lines as entries (DESIGN.md §18, "Editor logs", "Its lines"): a message and, for one from a script,
    /// its stack trace, <c>UnityEngine.Debug:LogWarning (object)</c> and the frames under it, then
    /// <c>(Filename: Assets/Player.cs Line: 12)</c>, which carry it on, muted, as an exception's frames do. The trace
    /// says what a script's message is: <c>LogWarning</c> a warning, <c>LogError</c>, <c>LogException</c> and
    /// <c>LogAssertion</c> an error. A blank line carries on the entry before it.
    /// </summary>
    private sealed partial class LineParser() : EditorLogParser(Classify, IsCrash)
    {
        protected override LineRead Read(string line, LogVerbosity? open)
        {
            if (open is null)
            {
                return new LineRead(Starts: true);
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                return new LineRead(Starts: false);
            }
            if (DebugCall().Match(line) is { Success: true } call)
            {
                var kind = call.Groups["kind"].ValueSpan;
                LogVerbosity? raises = kind is "Log" ? null : kind is "LogWarning" ? LogVerbosity.Warning : LogVerbosity.Error;
                return new LineRead(Starts: false, IsTrace: true, Raises: raises);
            }
            return Frame().IsMatch(line) ? new LineRead(Starts: false, IsTrace: true) : new LineRead(Starts: true);
        }

        [GeneratedRegex(@"^UnityEngine\.Debug:(?<kind>Log(?:Warning|Error|Exception|Assertion)?)(?:Format)?\s?\(")]
        private static partial Regex DebugCall();

        /// <summary>
        /// <c>Player:Start () (at Assets/Player.cs:12)</c>, <c>Player.Update ()</c>, Mono's <c>  at Player.Update ()</c>
        /// and <c>(Filename: Assets/Player.cs Line: 12)</c>; not a compiler's <c>Assets/Player.cs(12,5): error</c>.
        /// </summary>
        [GeneratedRegex(@"^[^\s()]*[:.][^\s()]*\s?\([^()]*\)(?:\s\(at\s[^()]*\))?\s*$|^\s+at\s|^\(Filename: .* Line: -?\d+\)\s*$")]
        private static partial Regex Frame();
    }

    [GeneratedRegex(@"\berror CS\d+\b|^[A-Za-z_][\w.]*Exception:|^(?:Error|ERROR)\b")]
    private static partial Regex ErrorLine();

    [GeneratedRegex(@"\bwarning CS\d+\b|^(?:Warning|WARNING)\b")]
    private static partial Regex WarningLine();
}
