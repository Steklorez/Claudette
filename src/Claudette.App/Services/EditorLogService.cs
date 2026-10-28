using Claudette.Core.ProjectTools;
using Claudette.Core.ProjectTools.EditorLogs;

namespace Claudette.App.Services;

/// <summary>A tab whose project's logs are followed, and whether a process is one it started, as when Claude ran a commandlet.</summary>
/// <param name="Started">Safe off the UI thread: looks through the tab's own processes and its project job's.</param>
public sealed record EditorLogTab(string TabId, string ProjectPath, Func<int, bool> Started);

/// <summary>A log being written, as a check found it.</summary>
/// <param name="NewWriters">Every process writing it is new since the last check: it's starting, or starting again.</param>
/// <param name="StartedBy">With <paramref name="NewWriters"/>, the tab one of them is a process of; null when none is.</param>
public sealed record EditorLogCheck(LiveLog Log, bool NewWriters, string? StartedBy);

/// <summary>
/// A log's processes moved to another file (DESIGN.md §18, "Editor logs", "A log that moves"), as Unity's editor does once
/// it has the project open: the log at <paramref name="From"/> stops there, and <paramref name="To"/> is being written.
/// </summary>
public sealed record EditorLogMove(string From, LiveLog To);

/// <summary>What a read of a log found: its new lines, and what's counted of them and of the lines passed over.</summary>
/// <param name="Fresh">The lines start the log afresh, in place of what was read before.</param>
/// <param name="Counts">Its entries at each level and in each category, and whether one says its process crashed.</param>
/// <param name="Skipped">The read passed over the start of a big file, which is in the file still.</param>
public sealed record EditorLogRead(IReadOnlyList<LogLine> Lines, bool Fresh, LogCounts Counts, bool Skipped)
{
    public int Errors => Counts.Errors;

    public int Warnings => Counts.Warnings;

    public bool Crashed => Counts.Crashed;
}

/// <summary>
/// Follows the logs of the tabs' projects' engines (DESIGN.md §18, "Editor logs"). Every second, off the UI thread, it
/// reads what's new in each log being written, opening it only for the read (<see cref="LogTail"/>); every
/// <see cref="ReadsPerCheck"/> seconds it looks at which of the projects' processes are running, with one
/// <see cref="ISystemProcesses.Find"/> for them all. It runs only while a tab's project has logs, and says what it finds
/// on the UI thread, a check before the reads after it. A log whose processes move to another file, as a line in it says
/// or a check finds, stops being read where it says so, and the other file is read from its start in its place.
/// </summary>
public sealed class EditorLogService : IDisposable
{
    public static readonly TimeSpan ReadInterval = TimeSpan.FromSeconds(1);

    /// <summary>Processes are looked at every this many reads: every 2 seconds, and every 5 on macOS, where each look runs <c>ps</c>.</summary>
    public static int ReadsPerCheck { get; } = OperatingSystem.IsMacOS() ? 5 : 2;

    private readonly AppServices _services;
    private readonly Lock _lock = new();
    private readonly EditorLogTracker _tracker;
    private readonly Dictionary<string, Followed> _logs;
    private IReadOnlyList<FollowedProject> _projects = [];
    private IReadOnlyList<EditorLogTab> _tabs = [];
    private ITimer? _timer;
    private long _ticks;
    private bool _busy;
    private bool _checkSoon;
    private bool _disposed;

    internal EditorLogService(AppServices services)
    {
        _services = services;
        var os = services.ProjectTools.OS;
        _tracker = new EditorLogTracker(services.Time.LocalTimeZone, os);
        _logs = new Dictionary<string, Followed>(os.PathComparer());
    }

    /// <summary>On the UI thread: which logs are being written now. Every check says, once it has something to say.</summary>
    public event Action<IReadOnlyList<EditorLogCheck>>? Checked;

    /// <summary>On the UI thread: what a read of the log at the path found.</summary>
    public event Action<string, EditorLogRead>? Read;

    /// <summary>
    /// On the UI thread: a log's processes write another file now. It comes after the old log's last read, and before
    /// the check and the reads that find the new one.
    /// </summary>
    public event Action<EditorLogMove>? Moved;

    /// <summary>
    /// The projects whose logs to follow and the tabs they're the projects of, from the UI thread whenever they change.
    /// Following none stops the reads, once a last check has said the logs aren't written any more.
    /// </summary>
    public void Follow(IReadOnlyList<FollowedProject> projects, IReadOnlyList<EditorLogTab> tabs)
    {
        ITimer? start = null;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            var wasFollowing = _projects.Count > 0;
            _projects = projects;
            _tabs = tabs;
            if (projects.Count > 0 && !wasFollowing)
            {
                // A look straight away, so an editor that's open already shows.
                _checkSoon = true;
            }
            if ((projects.Count > 0 || _logs.Count > 0 || wasFollowing) && _timer is null)
            {
                start = _timer = _services.Time.CreateTimer(_ => Tick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }
        // Started once the lock is let go: a clock can run the first read at once, on this thread, and what it finds goes
        // to the UI thread, which may be waiting here to follow other projects.
        try
        {
            start?.Change(TimeSpan.Zero, ReadInterval);
        }
        catch (ObjectDisposedException)
        {
            // Disposed meanwhile: Claudette is closing.
        }
    }

    /// <summary>Looks at the processes at the next read, rather than when it's their turn: one of the projects was just launched.</summary>
    public void CheckSoon()
    {
        lock (_lock)
        {
            _checkSoon = true;
        }
    }

    private void Tick()
    {
        IReadOnlyList<FollowedProject> projects;
        IReadOnlyList<EditorLogTab> tabs;
        bool check;
        lock (_lock)
        {
            if (_busy || _disposed)
            {
                return;
            }
            _busy = true;
            check = _checkSoon || _ticks % ReadsPerCheck == 0;
            _checkSoon = false;
            _ticks++;
            projects = _projects;
            tabs = _tabs;
        }
        try
        {
            if (check)
            {
                Check(projects, tabs);
            }
            ReadAll();
        }
        finally
        {
            lock (_lock)
            {
                _busy = false;
                if (_projects.Count == 0 && _logs.Count == 0 && _timer is { } timer)
                {
                    timer.Dispose();
                    _timer = null;
                    _ticks = 0;
                }
            }
        }
    }

    private void Check(IReadOnlyList<FollowedProject> projects, IReadOnlyList<EditorLogTab> tabs)
    {
        IReadOnlyList<SystemProcess> running = [];
        if (projects.Count > 0 && _services.ProjectTools.Processes is { } processes)
        {
            running = processes.Find(EditorLogTracker.ProcessNames(projects));
        }
        var live = _tracker.Update(projects, running, out var moves);
        foreach (var move in moves)
        {
            if (_logs.TryGetValue(move.From, out var from) && from.IsLive && SameProject(from.Project, move.Project))
            {
                // Its own processes write another file now, found before the line saying so was read: what's new in this
                // one, to that line if it's there, is its last.
                ReadLog(from, final: false, to =>
                {
                    if (_tracker.Holds(from.Project.Root, to))
                    {
                        return true;
                    }
                    _tracker.Move(from.Path, to);
                    return false;
                });
                MoveLog(from, move);
            }
        }
        var checks = new List<EditorLogCheck>(live.Count);
        var seen = new HashSet<string>(_logs.Comparer);
        foreach (var log in live)
        {
            seen.Add(log.Path);
            var writers = log.Writers.Select(w => w.Key).ToHashSet();
            var isNew = !_logs.TryGetValue(log.Path, out var followed) || !followed.IsLive || !followed.Writers.Overlaps(writers);
            if (followed is null || isNew)
            {
                // A log starting, or starting again: read from its start, as a new file.
                followed = new Followed(log.Path, log.Project, _services.Time.LocalTimeZone);
                _logs[log.Path] = followed;
            }
            followed.Project = log.Project;
            followed.IsLive = true;
            followed.Writers = writers;
            checks.Add(new EditorLogCheck(log, isNew, isNew ? StartedBy(log, tabs) : null));
        }
        foreach (var followed in _logs.Values)
        {
            if (followed.IsLive && !seen.Contains(followed.Path))
            {
                // Its writers have gone: one more read, for what they wrote last.
                followed.IsLive = false;
                followed.FinalRead = true;
            }
        }
        _services.Dispatcher.Post(() => Checked?.Invoke(checks));
    }

    /// <summary>The tab with the log's project whose own processes include one of its writers.</summary>
    private string? StartedBy(LiveLog log, IReadOnlyList<EditorLogTab> tabs)
    {
        var comparison = _services.ProjectTools.OS.PathComparison();
        foreach (var tab in tabs)
        {
            if (string.Equals(tab.ProjectPath, log.Project.ProjectPath, comparison) && log.Writers.Any(w => Started(tab, w.Process.Pid)))
            {
                return tab.TabId;
            }
        }
        return null;

        static bool Started(EditorLogTab tab, int pid)
        {
            try
            {
                return tab.Started(pid);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ObjectDisposedException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }

    private void ReadAll()
    {
        foreach (var followed in _logs.Values.ToArray())
        {
            if ((!followed.IsLive && !followed.FinalRead) || !IsFollowed(followed))
            {
                // Done with, or moved from earlier in this loop.
                continue;
            }
            var final = followed.FinalRead;
            followed.FinalRead = false;
            LogMove? own = null;
            ReadLog(followed, final, to =>
            {
                // A line saying a log moved: its processes write the file it names from now on.
                if (_tracker.Move(followed.Path, to) is not { } move || !SameProject(move.Project, followed.Project))
                {
                    return false;
                }
                own ??= move;
                return true;
            });
            if (own is not null)
            {
                MoveLog(followed, own);
            }
            else if (final && IsFollowed(followed))
            {
                _logs.Remove(followed.Path);
            }
        }
    }

    /// <summary>
    /// Reads what's new in a log and says so. <paramref name="isOwnMove"/> is told where each line saying it moved says
    /// it moved to, and says whether that's this log's processes' move: its lines stop at the first that is.
    /// </summary>
    private void ReadLog(Followed followed, bool final, Func<string, bool> isOwnMove)
    {
        var read = followed.Read(final, isOwnMove);
        if (read is not null)
        {
            _services.Dispatcher.Post(() => Read?.Invoke(followed.Path, read));
        }
    }

    /// <summary>
    /// The log's processes write <paramref name="move"/>'s file now: the log is read no more, and the other file is read
    /// from its start, already written by them, so the next check finds it carried on rather than starting.
    /// </summary>
    private void MoveLog(Followed from, LogMove move)
    {
        if (IsFollowed(from))
        {
            _logs.Remove(from.Path);
        }
        var writers = move.Writers.Select(w => w.Key).ToHashSet();
        if (_logs.TryGetValue(move.To, out var to) && to.IsLive)
        {
            writers.UnionWith(to.Writers);
        }
        else
        {
            to = new Followed(move.To, move.Project, _services.Time.LocalTimeZone);
            _logs[move.To] = to;
        }
        to.IsLive = true;
        to.Writers = writers;
        var moved = new EditorLogMove(from.Path, new LiveLog(move.To, move.Project, move.Writers));
        _services.Dispatcher.Post(() => Moved?.Invoke(moved));
    }

    /// <summary>Whether it's still the log being read at its path, not one moved from or done with.</summary>
    private bool IsFollowed(Followed followed) => _logs.TryGetValue(followed.Path, out var current) && ReferenceEquals(current, followed);

    private bool SameProject(FollowedProject a, FollowedProject b) =>
        string.Equals(a.ProjectPath, b.ProjectPath, _services.ProjectTools.OS.PathComparison());

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>One log being read, a line at a time as its entries, counting those its first read passes over.</summary>
    private sealed class Followed
    {
        private readonly LogTail _tail;
        private readonly EditorLogParser _parser;
        private readonly LogMoveReader? _moves;
        private bool _resetThisRead;

        public Followed(string path, FollowedProject project, TimeZoneInfo zone)
        {
            Path = path;
            Project = project;
            _parser = project.Source.NewParser(zone);
            _moves = project.Source.NewMoveReader?.Invoke();
            _tail = new LogTail(path, beforeWindow: line =>
            {
                // Only a fresh read passes lines over: the log starts afresh from its first.
                StartAfresh();
                _parser.Add(line, kept: false);
            });
        }

        public string Path { get; }

        /// <summary>The project whose processes write it, whose folder a move to a file in it is this log's own.</summary>
        public FollowedProject Project { get; set; }

        public bool IsLive { get; set; }

        /// <summary>Its writers have gone since the last read.</summary>
        public bool FinalRead { get; set; }

        public HashSet<(int Pid, DateTimeOffset? StartedAt)> Writers { get; set; } = [];

        /// <summary>
        /// What's new, read as entries and counted; null when there's nothing to say. Each line saying the log moved goes
        /// to <paramref name="isOwnMove"/>, and the lines stop at the first it says is this log's own.
        /// </summary>
        public EditorLogRead? Read(bool final, Func<string, bool> isOwnMove)
        {
            _resetThisRead = false;
            var read = _tail.Read(final);
            var lines = read.Lines;
            if (_moves is { } reader)
            {
                var stop = -2;
                foreach (var move in reader.Read(lines, read.Fresh, final))
                {
                    // Each is told, so another project's move after this one's still moves that one.
                    if (isOwnMove(move.To) && stop == -2)
                    {
                        stop = move.Line;
                    }
                }
                if (stop > -2)
                {
                    lines = lines.Take(stop + 1).ToArray();
                }
            }
            if (lines.Count == 0 && !read.Fresh)
            {
                return null;
            }
            if (read.Fresh)
            {
                StartAfresh();
            }
            foreach (var line in lines)
            {
                _parser.Add(line);
            }
            return new EditorLogRead(_parser.TakeLines(), read.Fresh, _parser.TakeCounts(), read.Skipped > 0);
        }

        private void StartAfresh()
        {
            if (!_resetThisRead)
            {
                _resetThisRead = true;
                _parser.Reset();
            }
        }
    }
}
