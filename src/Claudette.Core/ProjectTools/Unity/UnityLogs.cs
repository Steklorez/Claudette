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
    };

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

    [GeneratedRegex(@"\berror CS\d+\b|^[A-Za-z_][\w.]*Exception:|^(?:Error|ERROR)\b")]
    private static partial Regex ErrorLine();

    [GeneratedRegex(@"\bwarning CS\d+\b|^(?:Warning|WARNING)\b")]
    private static partial Regex WarningLine();
}
