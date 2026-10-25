using System.ComponentModel;
using System.Globalization;
using Claudette.Core.Diffs;
using Claudette.Core.Processes;
using Claudette.Core.ProjectFiles;

namespace Claudette.Core.Git;

public enum GitChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
    Untracked,
}

/// <summary>
/// A file that differs between HEAD and the working tree. Paths are absolute. The line counts are null for binary
/// files, and for untracked files too large to count.
/// </summary>
/// <param name="OldPath">The path in HEAD, for <see cref="GitChangeKind.Renamed"/>.</param>
public sealed record GitChange(string Path, GitChangeKind Kind, string? OldPath, int? Added, int? Removed)
{
    /// <summary>
    /// What changed that the lines don't show: its line endings, or its mode (DESIGN.md §8, "What the lines don't
    /// show"). The counts then leave out lines that changed only in their line endings, as the diff view does.
    /// </summary>
    public HiddenChanges? Hidden { get; init; }
}

/// <summary>
/// The "working tree vs HEAD" view of a repository (DESIGN.md §8), which also shows changes made by Bash commands or by
/// the user. Runs <c>git</c> from <c>PATH</c>. Every call has a timeout, and returns null or empty instead of throwing
/// when git is missing, times out, or the folder isn't in a repository.
/// </summary>
/// <param name="environment">
/// The user environment git runs with, and whose <c>PATH</c> it's found on (DESIGN.md §13), so git and its helpers
/// (Git LFS, credential helpers) are the ones a terminal would use. Null: Claudette's own.
/// </param>
public sealed class GitWorkingTree(IProcessLauncher launcher, TimeProvider timeProvider, string gitExecutable = "git", UserEnvironment? environment = null)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>Untracked files larger than this aren't read to count their lines.</summary>
    public long MaxCountBytes { get; init; } = TextFiles.DefaultMaxBytes;

    /// <summary>The top folder of the repository containing <paramref name="folder"/>, or null.</summary>
    public async Task<string?> GetRepositoryRootAsync(string folder, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(folder, ["rev-parse", "--show-toplevel"], cancellationToken).ConfigureAwait(false);
        if (result is not { ExitCode: 0 })
        {
            return null;
        }
        var root = result.StandardOutput.Split('\n', 2)[0].TrimEnd('\r');
        try
        {
            return root.Length == 0 ? null : Path.GetFullPath(root);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// The repository's top folder as git reports it, to run git from, and as <paramref name="folder"/> spells it, to put
    /// the paths git lists under. Git resolves symlinks, so on macOS the top of a folder under <c>/var</c> is under
    /// <c>/private/var</c>, and paths under it wouldn't match the same files as Claude and the tab name them. When the
    /// folder's path doesn't end in its path from the top, as when the folder is a symlink inside the repository, both
    /// are git's. Null when the folder isn't in a repository.
    /// </summary>
    private async Task<(string Root, string PathRoot)?> GetRootsAsync(string folder, CancellationToken cancellationToken)
    {
        var result = await RunAsync(folder, ["rev-parse", "--show-toplevel", "--show-prefix"], cancellationToken).ConfigureAwait(false);
        if (result is not { ExitCode: 0 })
        {
            return null;
        }
        var lines = result.StandardOutput.Split('\n', 3);
        var top = lines[0].TrimEnd('\r');
        var prefix = lines.Length > 1 ? lines[1].TrimEnd('\r') : "";
        try
        {
            if (top.Length == 0)
            {
                return null;
            }
            var root = Path.GetFullPath(top);
            var pathRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            foreach (var name in prefix.Split('/', StringSplitOptions.RemoveEmptyEntries).Reverse())
            {
                if (!ChangedFiles.PlatformPathComparer.Equals(Path.GetFileName(pathRoot), name) || Path.GetDirectoryName(pathRoot) is not { } parent)
                {
                    return (root, root);
                }
                pathRoot = parent;
            }
            return (root, pathRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Every file that differs from HEAD, including untracked ones, across the whole repository. In a repository with no
    /// commits yet, everything is added. Paths are spelled the way <paramref name="folder"/> is where that can be told,
    /// so they match Claude's changes to the same files (see <see cref="GetRootsAsync"/>).
    /// </summary>
    public async Task<IReadOnlyList<GitChange>> GetChangesAsync(string folder, CancellationToken cancellationToken = default)
    {
        if (await GetRootsAsync(folder, cancellationToken).ConfigureAwait(false) is not (var root, var pathRoot))
        {
            return [];
        }
        // Run from the top: status paths are relative to it anyway, and then diff's are too, whatever diff.relative says.
        var hasHead = await RunAsync(root, ["rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken).ConfigureAwait(false) is { ExitCode: 0 };
        var statusTask = RunAsync(root, ["--no-optional-locks", "status", "--porcelain=v1", "-z", "--untracked-files=all"], cancellationToken);
        var numstatTask = hasHead
            ? RunAsync(root, ["--no-optional-locks", "diff", "--raw", "--numstat", "-z", "-M", "--no-ext-diff", "--no-textconv", "--no-color", "HEAD", "--"], cancellationToken)
            : Task.FromResult<ProcessResult?>(null);
        // The same counts with carriage returns at the ends of lines ignored, which git also takes to cover the newline at
        // the end: what's left is what the diff view shows (DESIGN.md §8, "What the lines don't show").
        var linesTask = hasHead
            ? RunAsync(root, ["--no-optional-locks", "diff", "--numstat", "-z", "-M", "--ignore-cr-at-eol", "--no-ext-diff", "--no-textconv", "--no-color", "HEAD", "--"], cancellationToken)
            : Task.FromResult<ProcessResult?>(null);
        var status = await statusTask.ConfigureAwait(false);
        var numstat = await numstatTask.ConfigureAwait(false);
        var lineCounts = await linesTask.ConfigureAwait(false) is { ExitCode: 0 } counted ? ParseDiff(counted.StandardOutput).Counts : null;
        if (status is not { ExitCode: 0 })
        {
            return [];
        }
        var (counts, modes) = numstat is { ExitCode: 0 } ? ParseDiff(numstat.StandardOutput) : ([], []);

        var changes = new List<GitChange>();
        // The files whose line endings changed and nothing in the index, by their path in the repository: the index has
        // HEAD's line endings, so git can say what they were.
        var lineEndings = new List<(int Index, string Path)>();
        foreach (var (x, y, path, originalPath) in ParseStatus(status.StandardOutput))
        {
            var kind = Classify(x, y, originalPath is not null, hasHead);
            if (kind is null)
            {
                continue;
            }
            var reportedPath = kind == GitChangeKind.Deleted && originalPath is not null ? originalPath : path;
            var absolute = Absolute(pathRoot, reportedPath);
            (int? Added, int? Removed) lines = kind is GitChangeKind.Untracked || (kind is GitChangeKind.Added && !hasHead)
                ? CountAllAdded(absolute)
                : counts.TryGetValue(reportedPath, out var found) ? found : (null, null);
            HiddenChanges? hidden = null;
            if (kind is GitChangeKind.Modified or GitChangeKind.Renamed && lines is (int, int) && lineCounts is not null)
            {
                // Not listed: no line changed but at its end.
                var withoutEndings = lineCounts.TryGetValue(reportedPath, out var other) ? other : (0, 0);
                if (withoutEndings is (int, int) && withoutEndings != lines)
                {
                    hidden = new HiddenChanges { LineEndings = new LineEndingChange(null, null) };
                    lines = withoutEndings;
                    if (x == ' ')
                    {
                        lineEndings.Add((changes.Count, reportedPath));
                    }
                }
            }
            if (kind is GitChangeKind.Modified && modes.TryGetValue(reportedPath, out var mode))
            {
                hidden = (hidden ?? new HiddenChanges()) with { Mode = mode };
            }
            var oldPath = kind == GitChangeKind.Renamed && originalPath is not null ? Absolute(pathRoot, originalPath) : null;
            changes.Add(new GitChange(absolute, kind.Value, oldPath, lines.Added, lines.Removed) { Hidden = hidden });
        }
        if (lineEndings.Count > 0)
        {
            await SayWhichLineEndingsAsync(root, changes, lineEndings, cancellationToken).ConfigureAwait(false);
        }
        return changes;
    }

    /// <summary>
    /// What the line endings of <paramref name="lineEndings"/>' files were and are, from <c>git ls-files --eol</c>: the
    /// index's, which are HEAD's for a file with nothing staged, and the working tree's. When they're the same, what
    /// changed is the newline at the end.
    /// </summary>
    private async Task SayWhichLineEndingsAsync(string root, List<GitChange> changes, List<(int Index, string Path)> lineEndings, CancellationToken cancellationToken)
    {
        var kinds = new Dictionary<string, (LineEndingKind? Index, LineEndingKind? WorkTree)>(StringComparer.Ordinal);
        foreach (var batch in lineEndings.Chunk(200))
        {
            var result = await RunAsync(root, ["--no-optional-locks", "ls-files", "--eol", "-z", "--", .. batch.Select(b => ":(literal)" + b.Path)], cancellationToken).ConfigureAwait(false);
            if (result is not { ExitCode: 0 })
            {
                return;
            }
            foreach (var entry in TrimOutput(result.StandardOutput).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                // i/crlf  w/lf  attr/text=auto  \tpath
                var tab = entry.IndexOf('\t', StringComparison.Ordinal);
                if (tab < 0)
                {
                    continue;
                }
                var fields = entry[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                kinds[entry[(tab + 1)..]] = (Kind(fields, "i/"), Kind(fields, "w/"));
            }
        }
        foreach (var (index, path) in lineEndings)
        {
            if (!kinds.TryGetValue(path, out var kind) || kind is not ({ } before, { } now) || changes[index].Hidden is not { } hidden)
            {
                continue;
            }
            if (before != now && before != LineEndingKind.None && now != LineEndingKind.None)
            {
                changes[index] = changes[index] with { Hidden = hidden with { LineEndings = new LineEndingChange(before, now) } };
            }
            else if (before != LineEndingKind.Mixed && EndsWithNewline(changes[index].Path) is { } endsWithOne)
            {
                // The same line endings, or none on one side: only the last line's can have changed.
                changes[index] = changes[index] with { Hidden = hidden with { LineEndings = null, NewlineAtEnd = endsWithOne } };
            }
        }

        static LineEndingKind? Kind(string[] fields, string prefix) =>
            fields.FirstOrDefault(f => f.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..] switch
            {
                "lf" => LineEndingKind.Lf,
                "crlf" => LineEndingKind.Crlf,
                "mixed" => LineEndingKind.Mixed,
                "none" => LineEndingKind.None,
                // -text (binary), or empty (not in the index or the working tree).
                _ => null,
            };
    }

    /// <summary>Whether the file ends with a newline, reading only its last byte; null when it can't be read.</summary>
    private static bool? EndsWithNewline(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length == 0)
            {
                return null;
            }
            file.Seek(-1, SeekOrigin.End);
            return file.ReadByte() is '\n' or '\r';
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The file's content in HEAD, or null when it isn't in HEAD (or there's no HEAD). Git's output arrives line by line,
    /// so line endings come back as <c>\n</c> and non-empty text always ends with one; <see cref="LineDiff"/> ignores both.
    /// </summary>
    public async Task<string?> GetHeadContentAsync(string folder, string absolutePath, CancellationToken cancellationToken = default)
    {
        if (await GetRepositoryRootAsync(folder, cancellationToken).ConfigureAwait(false) is not { } root)
        {
            return null;
        }
        // Relative to the top (as git reports it) when possible; otherwise relative to the folder, which git resolves
        // itself. That covers a path spelled through a symlink, such as /var and /private/var on macOS.
        string revision;
        if (Inside(Path.GetRelativePath(root, absolutePath)) is { } fromRoot)
        {
            revision = "HEAD:" + fromRoot;
        }
        else if (Relative(Path.GetRelativePath(folder, absolutePath)) is { } fromFolder)
        {
            revision = fromFolder.StartsWith("../", StringComparison.Ordinal) ? "HEAD:" + fromFolder : "HEAD:./" + fromFolder;
        }
        else
        {
            return null;
        }

        var result = await RunAsync(folder, ["show", "--no-textconv", revision], cancellationToken).ConfigureAwait(false);
        return result is { ExitCode: 0 } ? result.StandardOutput.Replace(Environment.NewLine, "\n", StringComparison.Ordinal) : null;
    }

    /// <summary>
    /// The files under <paramref name="folder"/> that git doesn't ignore, tracked or not, relative to it with forward
    /// slashes, for the composer's <c>@</c> autocomplete (DESIGN.md §5). Null when the folder isn't in a repository.
    /// </summary>
    public async Task<IReadOnlyList<string>?> ListFilesAsync(string folder, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(folder, ["--no-optional-locks", "ls-files", "--cached", "--others", "--exclude-standard", "-z"], cancellationToken).ConfigureAwait(false);
        if (result is not { ExitCode: 0 })
        {
            return null;
        }
        return TrimOutput(result.StandardOutput).Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Which of <paramref name="paths"/>, relative to <paramref name="folder"/> with forward slashes (a folder's with one
    /// at its end), git ignores, for the Project Files tab's explorer (DESIGN.md §18, "Project files"). A tracked file is
    /// never ignored. Null when the folder isn't in a repository, or git can't say.
    /// </summary>
    public async Task<IReadOnlySet<string>?> CheckIgnoredAsync(string folder, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        var ignored = new HashSet<string>(StringComparer.Ordinal);
        // A batch at a time, well inside a command line's limit.
        foreach (var batch in paths.Chunk(200))
        {
            // Not -z: git takes that only with --stdin. A name git has to quote (with a tab or a quote mark in it) doesn't
            // match, so it shows as not ignored.
            var result = await RunAsync(folder, ["--no-optional-locks", "check-ignore", "--", .. batch], cancellationToken).ConfigureAwait(false);
            // 0: some are ignored; 1: none are; anything else, git couldn't tell (not a repository, say).
            if (result is not { ExitCode: 0 or 1 })
            {
                return null;
            }
            foreach (var path in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                ignored.Add(path.TrimEnd('\r'));
            }
        }
        return ignored;
    }

    /// <summary>Whether anything differs from HEAD, including untracked files. Null when it can't be told.</summary>
    public async Task<bool?> HasUncommittedChangesAsync(string folder, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(folder, ["--no-optional-locks", "status", "--porcelain=v1", "-z", "--untracked-files=normal"], cancellationToken).ConfigureAwait(false);
        return result is { ExitCode: 0 } ? TrimOutput(result.StandardOutput).Length > 0 : null;
    }

    internal Task<ProcessResult?> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        RunAsync(workingDirectory, arguments, Timeout, cancellationToken);

    /// <param name="timeout">How long git may take, for a command that can take longer than most (deleting a worktree).</param>
    /// <param name="variables">Set over the user environment, such as to keep git from asking for a password.</param>
    internal async Task<ProcessResult?> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? variables = null)
    {
        try
        {
            if (!Directory.Exists(workingDirectory))
            {
                return null;
            }
            var spec = new ProcessStartSpec(gitExecutable, ["-c", "core.quotepath=false", .. arguments]) { WorkingDirectory = workingDirectory };
            if (environment is not null)
            {
                spec = await environment.ApplyAsync(spec, cancellationToken).ConfigureAwait(false);
            }
            if (variables is not null)
            {
                spec = UserEnvironment.WithVariables(spec, variables);
            }
            return await ProcessRunner.RunAsync(launcher, spec, timeout, timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// What an entry of <c>git status --porcelain=v1</c> means relative to HEAD; null for nothing (added and then
    /// deleted again, or ignored). X is the index column and Y the working tree column.
    /// </summary>
    private static GitChangeKind? Classify(char x, char y, bool hasOriginalPath, bool hasHead)
    {
        if (x == '?' && y == '?')
        {
            return GitChangeKind.Untracked;
        }
        if (x == '!')
        {
            return null;
        }
        if (x == 'U' || y == 'U' || (x == 'A' && y == 'A') || (x == 'D' && y == 'D'))
        {
            return GitChangeKind.Modified; // A merge conflict.
        }
        if (!hasHead)
        {
            return y == 'D' ? null : GitChangeKind.Added;
        }
        if (x == 'R' || y == 'R')
        {
            return y == 'D' ? GitChangeKind.Deleted : hasOriginalPath ? GitChangeKind.Renamed : GitChangeKind.Added;
        }
        if (x is 'A' or 'C' || y is 'A' or 'C')
        {
            return y == 'D' ? null : GitChangeKind.Added;
        }
        if (x == 'D' || y == 'D')
        {
            return GitChangeKind.Deleted;
        }
        return GitChangeKind.Modified;
    }

    /// <summary>
    /// Reads <c>git status --porcelain=v1 -z</c>: <c>XY path\0</c>, with the original path in the next field for a rename
    /// or copy.
    /// </summary>
    private static IEnumerable<(char X, char Y, string Path, string? OriginalPath)> ParseStatus(string output)
    {
        var fields = TrimOutput(output).Split('\0');
        for (var i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            if (field.Length < 4 || field[2] != ' ')
            {
                continue;
            }
            char x = field[0], y = field[1];
            string? original = null;
            if ((x is 'R' or 'C' || y is 'R' or 'C') && i + 1 < fields.Length)
            {
                original = fields[++i];
            }
            yield return (x, y, field[3..], original);
        }
    }

    /// <summary>
    /// Reads <c>git diff --numstat -z</c>: <c>added\tremoved\tpath\0</c>, or for a rename
    /// <c>added\tremoved\t\0old\0new\0</c>. Binary files show <c>-</c> for both counts. With <c>--raw</c> first, each file's
    /// <c>:oldmode newmode oldsha newsha status\0path\0</c> (two paths for a rename or copy) comes before them, which
    /// gives the files whose mode changed, by their path now.
    /// </summary>
    private static (Dictionary<string, (int?, int?)> Counts, Dictionary<string, ModeChange> Modes) ParseDiff(string output)
    {
        var counts = new Dictionary<string, (int?, int?)>(StringComparer.Ordinal);
        var modes = new Dictionary<string, ModeChange>(StringComparer.Ordinal);
        var fields = TrimOutput(output).Split('\0');
        for (var i = 0; i < fields.Length; i++)
        {
            if (fields[i].StartsWith(':'))
            {
                var raw = fields[i][1..].Split(' ');
                var paths = raw.Length == 5 && raw[4].Length > 0 && raw[4][0] is 'R' or 'C' ? 2 : 1;
                if (i + paths >= fields.Length)
                {
                    break;
                }
                i += paths;
                // A file added or deleted has 000000 on the side it isn't on.
                if (raw.Length == 5 && raw[0] != raw[1] && raw[0] != "000000" && raw[1] != "000000")
                {
                    modes[fields[i]] = new ModeChange(raw[0], raw[1]);
                }
                continue;
            }
            var parts = fields[i].Split('\t', 3);
            if (parts.Length < 3)
            {
                continue;
            }
            var path = parts[2];
            if (path.Length == 0)
            {
                if (i + 2 >= fields.Length)
                {
                    break;
                }
                path = fields[i + 2];
                i += 2;
            }
            counts[path] = (Number(parts[0]), Number(parts[1]));
        }
        return (counts, modes);

        static int? Number(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private (int?, int?) CountAllAdded(string path)
    {
        if (Path.EndsInDirectorySeparator(path))
        {
            return (null, null); // A nested repository.
        }
        var read = TextFiles.Read(path, MaxCountBytes);
        return read.Kind == TextFileKind.Text ? (LineDiff.CountLines(read.Text), 0) : (null, null);
    }

    /// <summary>ProcessRunner ends the output with a newline of its own.</summary>
    private static string TrimOutput(string output) =>
        output.EndsWith(Environment.NewLine, StringComparison.Ordinal) ? output[..^Environment.NewLine.Length] : output;

    private static string Absolute(string root, string repoRelative) =>
        Path.GetFullPath(Path.Combine(root, repoRelative.Replace('/', Path.DirectorySeparatorChar)));

    private static string? Inside(string relative) =>
        Relative(relative) is { } path && path != ".." && !path.StartsWith("../", StringComparison.Ordinal) ? path : null;

    private static string? Relative(string relative) =>
        relative == "." || Path.IsPathRooted(relative) ? null : relative.Replace(Path.DirectorySeparatorChar, '/');
}
