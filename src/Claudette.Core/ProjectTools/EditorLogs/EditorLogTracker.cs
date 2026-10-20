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
/// Which logs the followed projects' processes are writing (DESIGN.md §18, "Editor logs"), from the processes running
/// now. A process whose engine leaves it several files to choose from (Unreal takes <c>Name_2.log</c> while another has
/// <c>Name.log</c>) is given the one opened soonest after it started that no other process has, and keeps it while it
/// runs. One whose file isn't there yet is looked at again next time.
/// </summary>
public sealed class EditorLogTracker(TimeZoneInfo zone, ToolOS os)
{
    /// <summary>A log is opened a moment after its process starts, and its first line gives the time to the second.</summary>
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(5);

    /// <summary>The file each process took, of several it could have, once it's known.</summary>
    private readonly Dictionary<(int Pid, DateTimeOffset? StartedAt), string> _chosen = [];

    /// <summary>The processes to look for: every followed project's engine's.</summary>
    public static IReadOnlyCollection<string> ProcessNames(IEnumerable<FollowedProject> projects) =>
        projects.SelectMany(p => p.Source.ProcessNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>The logs being written now, one for each file, with the processes writing it, oldest first.</summary>
    public IReadOnlyList<LiveLog> Update(IReadOnlyList<FollowedProject> projects, IReadOnlyList<SystemProcess> running)
    {
        var alive = running.Select(p => (p.Pid, p.StartedAt)).ToHashSet();
        foreach (var gone in _chosen.Keys.Where(k => !alive.Contains(k)).ToArray())
        {
            _chosen.Remove(gone);
        }
        var taken = new HashSet<string>(_chosen.Values, os.PathComparer());
        var logs = new Dictionary<string, (FollowedProject Project, List<LogWriterProcess> Writers)>(os.PathComparer());
        var order = new List<string>();
        foreach (var process in running.OrderBy(p => p.StartedAt ?? DateTimeOffset.MaxValue).ThenBy(p => p.Pid))
        {
            foreach (var project in projects)
            {
                if (!SystemProcessNames.Matches(process.Name, project.Source.ProcessNames) || project.Source.Match(process) is not { } writer)
                {
                    continue;
                }
                var path = writer.LogFiles.Count == 1 ? writer.LogFiles[0] : Choose(process, project.Source, writer.LogFiles, taken);
                if (path is not null)
                {
                    if (!logs.TryGetValue(path, out var log))
                    {
                        logs[path] = log = (project, []);
                        order.Add(path);
                    }
                    log.Writers.Add(new LogWriterProcess(process, writer.Role));
                }
                // A process is one project's.
                break;
            }
        }
        return order.Select(path => new LiveLog(path, logs[path].Project, logs[path].Writers)).ToArray();
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
}
