namespace Claudette.Core.ProjectTools.EditorLogs;

/// <summary>A project whose engine's logs are followed: the tabs' projects that have them.</summary>
/// <param name="ProjectPath">Its <see cref="ProjectInfo.ProjectPath"/>, which says which tabs it's the project of.</param>
/// <param name="Root">Its folder, which a relative path in its logs is read against.</param>
public sealed record FollowedProject(string ProjectPath, string Root, EditorLogSource Source);

/// <summary>A process writing a log, and what it is.</summary>
public sealed record LogWriterProcess(SystemProcess Process, string Role)
{
    /// <summary>The process, told apart from a later one that reuses its PID.</summary>
    public (int Pid, DateTimeOffset? StartedAt) Key => (Process.Pid, Process.StartedAt);
}

/// <summary>A log some of the project's processes are writing now.</summary>
public sealed record LiveLog(string Path, FollowedProject Project, IReadOnlyList<LogWriterProcess> Writers);

/// <summary>
/// A project's processes writing another file in place of the one they wrote (DESIGN.md §18, "Editor logs", "A log that
/// moves"), as Unity's editor does once it has the project open.
/// </summary>
/// <param name="From">The file they wrote.</param>
/// <param name="To">The file they write now.</param>
/// <param name="Project">Theirs.</param>
public sealed record LogMove(string From, string To, FollowedProject Project, IReadOnlyList<LogWriterProcess> Writers);

/// <summary>
/// Which logs the followed projects' processes are writing (DESIGN.md §18, "Editor logs"), from the processes running
/// now. A process whose engine leaves it several files to choose from (Unreal takes <c>Name_2.log</c> while another has
/// <c>Name.log</c>) is given the one opened soonest after it started that no other process has, and keeps it while it
/// runs. One whose file isn't there yet is looked at again next time. A process whose log moves to another file, as a
/// line in it says (<see cref="Move"/>) or as its engine says at a later look, writes that one from then on.
/// </summary>
public sealed class EditorLogTracker(TimeZoneInfo zone, ToolOS os)
{
    /// <summary>A log is opened a moment after its process starts, and its first line gives the time to the second.</summary>
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(5);

    /// <summary>The file each process took, of several it could have, once it's known.</summary>
    private readonly Dictionary<(int Pid, DateTimeOffset? StartedAt), string> _chosen = [];

    /// <summary>The file each process whose log moved writes now, which it keeps to while it runs.</summary>
    private readonly Dictionary<(int Pid, DateTimeOffset? StartedAt), string> _moved = [];

    /// <summary>What each process was writing at the last look.</summary>
    private Dictionary<(int Pid, DateTimeOffset? StartedAt), Writing> _writing = [];

    /// <summary>The processes to look for: every followed project's engine's.</summary>
    public static IReadOnlyCollection<string> ProcessNames(IEnumerable<FollowedProject> projects) =>
        projects.SelectMany(p => p.Source.ProcessNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>The logs being written now, one for each file, with the processes writing it, oldest first.</summary>
    public IReadOnlyList<LiveLog> Update(IReadOnlyList<FollowedProject> projects, IReadOnlyList<SystemProcess> running) =>
        Update(projects, running, out _);

    /// <summary>
    /// The logs being written now, and <paramref name="moves"/>: processes found writing another file than at the last
    /// look, as Unity's editor does once it has the project open, without a line saying so having been read.
    /// </summary>
    public IReadOnlyList<LiveLog> Update(IReadOnlyList<FollowedProject> projects, IReadOnlyList<SystemProcess> running, out IReadOnlyList<LogMove> moves)
    {
        var alive = running.Select(p => (p.Pid, p.StartedAt)).ToHashSet();
        foreach (var gone in _chosen.Keys.Where(k => !alive.Contains(k)).ToArray())
        {
            _chosen.Remove(gone);
        }
        foreach (var gone in _moved.Keys.Where(k => !alive.Contains(k)).ToArray())
        {
            _moved.Remove(gone);
        }
        var comparison = os.PathComparison();
        var taken = new HashSet<string>(_chosen.Values, os.PathComparer());
        var logs = new Dictionary<string, (FollowedProject Project, List<LogWriterProcess> Writers)>(os.PathComparer());
        var order = new List<string>();
        var writing = new Dictionary<(int Pid, DateTimeOffset? StartedAt), Writing>();
        var found = new List<LogMove>();
        foreach (var process in running.OrderBy(p => p.StartedAt ?? DateTimeOffset.MaxValue).ThenBy(p => p.Pid))
        {
            foreach (var project in projects)
            {
                if (!SystemProcessNames.Matches(process.Name, project.Source.ProcessNames) || project.Source.Match(process) is not { } writer)
                {
                    continue;
                }
                var key = (process.Pid, process.StartedAt);
                var path = _moved.TryGetValue(key, out var moved) ? moved
                    : writer.LogFiles.Count == 1 ? writer.LogFiles[0]
                    : Choose(process, project.Source, writer.LogFiles, taken);
                if (path is not null)
                {
                    if (!logs.TryGetValue(path, out var log))
                    {
                        logs[path] = log = (project, []);
                        order.Add(path);
                    }
                    var writerProcess = new LogWriterProcess(process, writer.Role);
                    log.Writers.Add(writerProcess);
                    if (_writing.TryGetValue(key, out var was) && !string.Equals(was.Path, path, comparison))
                    {
                        // It writes another file now, and keeps to it while it runs.
                        _moved[key] = path;
                        Add(found, new LogMove(was.Path, path, project, [writerProcess]));
                    }
                    writing[key] = new Writing(path, project, writerProcess, writer.CanMove && !_moved.ContainsKey(key));
                }
                // A process is one project's.
                break;
            }
        }
        _writing = writing;
        moves = found;
        return order.Select(path => new LiveLog(path, logs[path].Project, logs[path].Writers)).ToArray();
    }

    /// <summary>
    /// A line in <paramref name="from"/> said it moved to <paramref name="to"/>, as Unity's machine-wide <c>Editor.log</c>
    /// says once an editor has its project open. The processes writing <paramref name="from"/> at the last look whose log
    /// can move, of the project whose folder holds <paramref name="to"/>, write <paramref name="to"/> from now on. Null
    /// when there are none: it's the move of a project that isn't followed, or of one whose processes keep to their log.
    /// </summary>
    public LogMove? Move(string from, string to)
    {
        var comparison = os.PathComparison();
        if (string.Equals(from, to, comparison))
        {
            return null;
        }
        var movers = _writing
            .Where(w => w.Value.CanMove && string.Equals(w.Value.Path, from, comparison) && Holds(w.Value.Project.Root, to))
            .ToArray();
        if (movers.Length == 0)
        {
            return null;
        }
        // The project in the deepest folder, should one be inside another.
        var project = movers.MaxBy(m => m.Value.Project.Root.Length).Value.Project;
        var writers = new List<LogWriterProcess>();
        foreach (var (key, was) in movers.Where(m => string.Equals(m.Value.Project.ProjectPath, project.ProjectPath, comparison)))
        {
            _moved[key] = to;
            _writing[key] = was with { Path = to, CanMove = false };
            writers.Add(was.Process);
        }
        return new LogMove(from, to, project, writers);
    }

    /// <summary>Whether <paramref name="path"/> is in <paramref name="folder"/> or a folder in it, with either slash.</summary>
    public bool Holds(string folder, string path)
    {
        var inside = Normal(folder);
        var file = Normal(path);
        return file.Length > inside.Length && file[inside.Length] == '/' && file.StartsWith(inside, os.PathComparison());
    }

    private static string Normal(string path) => path.Replace('\\', '/').TrimEnd('/');

    /// <summary>A move to add to <paramref name="moves"/>, into one with the same files and project when there is one.</summary>
    private void Add(List<LogMove> moves, LogMove move)
    {
        var comparison = os.PathComparison();
        var same = moves.FindIndex(m => string.Equals(m.From, move.From, comparison) && string.Equals(m.To, move.To, comparison)
            && string.Equals(m.Project.ProjectPath, move.Project.ProjectPath, comparison));
        if (same < 0)
        {
            moves.Add(move);
        }
        else
        {
            moves[same] = moves[same] with { Writers = [.. moves[same].Writers, .. move.Writers] };
        }
    }

    /// <summary>
    /// The file <paramref name="process"/> took, of <paramref name="files"/>: the one opened soonest after it started that
    /// no other has. Without the times to go by, the first that's there and free.
    /// </summary>
    private string? Choose(SystemProcess process, EditorLogSource source, IReadOnlyList<string> files, HashSet<string> taken)
    {
        if (_chosen.TryGetValue((process.Pid, process.StartedAt), out var chosen))
        {
            return chosen;
        }
        string? best = null;
        DateTimeOffset? bestOpened = null;
        foreach (var file in files)
        {
            if (taken.Contains(file) || !File.Exists(file))
            {
                continue;
            }
            DateTimeOffset? opened = null;
            if (process.StartedAt is { } started && source.OpenedAt is { } openedAt)
            {
                // A file that doesn't say yet is being started; one opened before the process is an earlier one's.
                if (openedAt(file, zone) is not { } at || at < started - Slack)
                {
                    continue;
                }
                opened = at;
            }
            if (best is null || opened < bestOpened)
            {
                best = file;
                bestOpened = opened;
            }
            if (opened is null)
            {
                break;
            }
        }
        if (best is not null)
        {
            _chosen[(process.Pid, process.StartedAt)] = best;
            taken.Add(best);
        }
        return best;
    }

    /// <summary>A process's log at the last look, its project, and whether a line in the log can move it.</summary>
    private sealed record Writing(string Path, FollowedProject Project, LogWriterProcess Process, bool CanMove);
}
