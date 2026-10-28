using System.Globalization;

namespace Claudette.Core.Perforce;

/// <summary>What a <c>p4</c> command did: whether it ran at all, and what it printed.</summary>
/// <param name="Ran">It started and finished in time; false for no <c>p4</c>, a missing folder or a timeout.</param>
public sealed record PerforceCommandResult(bool Ran, int ExitCode, string Output, string Error, bool TimedOut = false)
{
    public static PerforceCommandResult NotRun(bool timedOut = false) => new(false, -1, "", "", timedOut);

    /// <summary>Both streams, for the checks that look at either.</summary>
    public string Text => $"{Output}\n{Error}";

    /// <summary>The ticket has expired, or there's none: a login would let it work.</summary>
    public bool NeedsLogin => PerforceErrors.NeedsLogin(Text);

    /// <summary>The server didn't answer.</summary>
    public bool IsConnectionFailure => TimedOut || PerforceErrors.IsConnectionFailure(Text);
}

/// <summary>Which of Perforce's answers a command takes as nothing to tell the user.</summary>
public enum PerforceQuietness
{
    /// <summary>The user chose the command: anything Perforce refused is said, a file it doesn't have too.</summary>
    Asked,

    /// <summary>
    /// Claudette ran it by itself (DESIGN.md §18, "Opening files in Perforce"): a file Perforce doesn't have, or one
    /// outside the workspace's view, just means there was nothing to do.
    /// </summary>
    Automatic,
}

/// <summary>
/// The <c>p4</c> commands of Perforce in the Files tab and the automatic actions (DESIGN.md §18, "Perforce in the Files
/// tab"): their arguments, the file specs they name, and what their output means. Paths are local syntax, with Perforce's
/// wildcard characters (<c>@ # % *</c>) escaped, so a file named <c>a@2x.png</c> is that file and not a revision.
/// </summary>
public static class PerforceFileCommands
{
    /// <summary>A file, as a spec: its path with <c>@ # % *</c> escaped.</summary>
    public static string FileSpec(string path) => Escape(path);

    /// <summary>Everything under a folder: <c>folder/...</c>.</summary>
    public static string FolderSpec(string folder) => Escape(Path.TrimEndingDirectorySeparator(folder)) + Path.DirectorySeparatorChar + "...";

    /// <summary>The files directly in a folder: <c>folder/*</c>, which a folder's marks are asked with.</summary>
    public static string FilesInFolderSpec(string folder) => Escape(Path.TrimEndingDirectorySeparator(folder)) + Path.DirectorySeparatorChar + "*";

    /// <summary>A file or folder as a spec: a folder's is everything under it.</summary>
    public static string Spec(string path, bool isFolder) => isFolder ? FolderSpec(path) : FileSpec(path);

    /// <summary><c>-ztag fstat -T …</c>: the state of each file the specs name, with only the fields the marks need.</summary>
    public static IReadOnlyList<string> Fstat(IEnumerable<string> specs) => ["-ztag", "fstat", "-T", PerforceFileState.Fields, .. specs];

    /// <summary><c>edit [-c 12345] specs</c>: check out.</summary>
    public static IReadOnlyList<string> Edit(IEnumerable<string> specs, long? changelist) => ["edit", .. InChangelist(changelist), .. specs];

    /// <summary>
    /// <c>add [-c 12345] [-f] files</c>: mark for add. Perforce's own ignore rules (P4IGNORE) apply. Names with a wildcard
    /// character need <c>-f</c>, and are given as they are, not escaped.
    /// </summary>
    public static IReadOnlyList<string> Add(IReadOnlyCollection<string> files, long? changelist)
    {
        var force = files.Any(HasWildcard);
        return ["add", .. InChangelist(changelist), .. force ? ["-f"] : Array.Empty<string>(), .. force ? files : files.Select(FileSpec)];
    }

    /// <summary>
    /// <c>reconcile -a [-c 12345] folder/...</c>: a folder's <b>Mark for add</b>, since <c>p4 add</c> takes no <c>...</c>.
    /// It opens the files under it that Perforce doesn't have, skipping what P4IGNORE does.
    /// </summary>
    public static IReadOnlyList<string> ReconcileAdd(string folder, long? changelist) => ["reconcile", "-a", .. InChangelist(changelist), FolderSpec(folder)];

    /// <summary><c>delete [-c 12345] [-k] specs</c>: mark for delete; with <c>-k</c>, the local files are left alone.</summary>
    public static IReadOnlyList<string> Delete(IEnumerable<string> specs, long? changelist, bool keepLocal) =>
        ["delete", .. InChangelist(changelist), .. keepLocal ? ["-k"] : Array.Empty<string>(), .. specs];

    /// <summary><c>revert [-k] specs</c>; with <c>-k</c>, the local files are left alone.</summary>
    public static IReadOnlyList<string> Revert(IEnumerable<string> specs, bool keepLocal) =>
        ["revert", .. keepLocal ? ["-k"] : Array.Empty<string>(), .. specs];

    /// <summary><c>move -k [-c 12345] from to</c>: the move on the server, after the files were renamed on disk.</summary>
    public static IReadOnlyList<string> Move(string fromSpec, string toSpec, long? changelist) => ["move", "-k", .. InChangelist(changelist), fromSpec, toSpec];

    /// <summary><c>diff -sa specs</c>: the opened files that differ from the depot, which a revert would lose.</summary>
    public static IReadOnlyList<string> DiffChanged(IEnumerable<string> specs) => ["diff", "-sa", .. specs];

    /// <summary><c>print -q -o out file#have</c>: the revision synced here, written to a file as it is, line endings and all.</summary>
    public static IReadOnlyList<string> PrintHave(string path, string outputFile) => ["print", "-q", "-o", outputFile, FileSpec(path) + "#have"];

    /// <summary><c>set -q</c>: every Perforce setting for the folder, from its config file, <c>p4 set</c> and the environment.</summary>
    public static IReadOnlyList<string> Settings() => ["set", "-q"];

    private static IEnumerable<string> InChangelist(long? changelist) =>
        changelist is { } number ? ["-c", number.ToString(CultureInfo.InvariantCulture)] : [];

    public static bool HasWildcard(string path) => Path.GetFileName(path).AsSpan().IndexOfAny("@#%*") >= 0;

    /// <summary>Perforce's escapes for its wildcard characters, <c>%</c> first so the others' aren't escaped again.</summary>
    public static string Escape(string path) =>
        path.Replace("%", "%25", StringComparison.Ordinal).Replace("@", "%40", StringComparison.Ordinal)
            .Replace("#", "%23", StringComparison.Ordinal).Replace("*", "%2A", StringComparison.Ordinal);

    /// <summary>Undoes <see cref="Escape"/>, for the paths Perforce prints.</summary>
    public static string Unescape(string path) =>
        path.Contains('%', StringComparison.Ordinal)
            ? path.Replace("%40", "@", StringComparison.Ordinal).Replace("%23", "#", StringComparison.Ordinal)
                .Replace("%2A", "*", StringComparison.OrdinalIgnoreCase).Replace("%25", "%", StringComparison.Ordinal)
            : path;

    /// <summary>Each <c>NAME=value</c> line of <c>p4 set -q</c>.</summary>
    public static IReadOnlyDictionary<string, string> ParseSettings(string output)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                continue;
            }
            var name = line[..equals].Trim();
            if (PerforceWorkspace.ParseSetting(line, name) is { } value)
            {
                settings[name] = value;
            }
        }
        return settings;
    }

    /// <summary>
    /// Whether <c>p4 set</c> found a server or a workspace to use for the folder, from its config file, <c>p4 set</c>, the
    /// environment or P4ENVIRO. Without either, <c>p4</c> would try Perforce's defaults (<c>perforce:1666</c>, a workspace
    /// named after the machine), so Claudette doesn't ask the server (DESIGN.md §18, "Finding the workspace").
    /// </summary>
    public static bool IsConfigured(IReadOnlyDictionary<string, string> settings) =>
        settings.ContainsKey("P4PORT") || settings.ContainsKey("P4CLIENT");

    /// <summary>
    /// What went wrong, in Perforce's words, or null when nothing did. Perforce's exit code doesn't say (a file already
    /// open is a warning, a locked one an error), so each line is read: an error line counts unless it's one of the
    /// answers that just mean there was nothing to do, and an output line counts when Perforce says it <i>can't</i>.
    /// </summary>
    public static string? Problem(PerforceCommandResult result, PerforceQuietness quietness = PerforceQuietness.Asked)
    {
        if (!result.Ran)
        {
            return result.TimedOut ? "Perforce didn't answer in time." : "Claudette couldn't run p4.";
        }
        var problems = new List<string>();
        foreach (var raw in result.Error.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length > 0 && !IsNothingToDo(line, quietness) && !line.StartsWith("Perforce client error:", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(line);
            }
        }
        foreach (var raw in result.Output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Contains(" - can't ", StringComparison.OrdinalIgnoreCase) && !IsNothingToDo(line, quietness))
            {
                problems.Add(line);
            }
        }
        return problems.Count == 0 ? null : string.Join(" ", problems.Take(3)) + (problems.Count > 3 ? $" (and {problems.Count - 3} more)" : "");
    }

    /// <summary>The answers that mean the file was already as asked, or (for Claudette's own commands) not Perforce's.</summary>
    private static bool IsNothingToDo(string line, PerforceQuietness quietness)
    {
        string[] always =
        [
            "currently opened for",
            "already opened for",
            "also opened by",
            "file(s) not opened on this client",
            "file(s) not opened for edit",
            "ignored file can't be added",
            " - ignored file",
            "can't add existing file",
            "no file(s) to reconcile",
        ];
        string[] automatic =
        [
            "no such file(s)",
            "file(s) not on client",
            "not in client view",
            "is not under client's root",
            "not under client's root",
        ];
        return always.Any(t => line.Contains(t, StringComparison.OrdinalIgnoreCase))
            || quietness == PerforceQuietness.Automatic && automatic.Any(t => line.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}
