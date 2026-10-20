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

/// <summary>What a read of a log found: its new lines, and how many of them, and of the lines passed over, are errors and warnings.</summary>
/// <param name="Fresh">The lines start the log afresh, in place of what was read before.</param>
/// <param name="Crashed">One of them says its process crashed.</param>
/// <param name="Skipped">The read passed over the start of a big file, which is in the file still.</param>
public sealed record EditorLogRead(IReadOnlyList<string> Lines, bool Fresh, int Errors, int Warnings, bool Crashed, bool Skipped);

/// <summary>
/// Follows the logs of the tabs' projects' engines (DESIGN.md §18, "Editor logs"). Every second, off the UI thread, it
/// reads what's new in each log being written, opening it only for the read (<see cref="LogTail"/>); every
/// <see cref="ReadsPerCheck"/> seconds it looks at which of the projects' processes are running, with one
/// <see cref="ISystemProcesses.Find"/> for them all. It runs only while a tab's project has logs, and says what it finds
/// on the UI thread, a check before the reads after it.
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
    /// The projects whose logs to follow and the tabs they're the projects of, from the UI thread whenever they change.
    /// Following none stops the reads, once a last check has said the logs aren't written any more.
    /// </summary>
    public void Follow(IReadOnlyList<FollowedProject> projects, IReadOnlyList<EditorLogTab> tabs)
    {
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
            if (projects.Count > 0 || _logs.Count > 0 || wasFollowing)
            {
                _timer ??= _services.Time.CreateTimer(_ => Tick(), null, TimeSpan.Zero, ReadInterval);
            }
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
        var live = _tracker.Update(projects, running);
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
                followed = new Followed(log.Path, log.Project.Source);
                _logs[log.Path] = followed;
            }
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
            if (!followed.IsLive && !followed.FinalRead)
            {
                continue;
            }
            var final = followed.FinalRead;
            followed.FinalRead = false;
            var read = followed.Read(final);
            if (read is not null)
            {
                _services.Dispatcher.Post(() => Read?.Invoke(followed.Path, read));
            }
            if (final)
            {
                _logs.Remove(followed.Path);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>One log being read, with what's counted of the lines its first read passed over.</summary>
    private sealed class Followed
    {
        private readonly LogTail _tail;
        private readonly EditorLogSource _source;
        private int _skippedErrors;
        private int _skippedWarnings;
        private bool _skippedCrash;

        public Followed(string path, EditorLogSource source)
        {
            Path = path;
            _source = source;
            _tail = new LogTail(path, beforeWindow: line =>
            {
                Count(line, ref _skippedErrors, ref _skippedWarnings, ref _skippedCrash);
            });
        }

        public string Path { get; }

        public bool IsLive { get; set; }

        /// <summary>Its writers have gone since the last read.</summary>
        public bool FinalRead { get; set; }

        public HashSet<(int Pid, DateTimeOffset? StartedAt)> Writers { get; set; } = [];

        /// <summary>What's new, counted; null when there's nothing to say.</summary>
        public EditorLogRead? Read(bool final)
        {
            _skippedErrors = 0;
            _skippedWarnings = 0;
            _skippedCrash = false;
            var read = _tail.Read(final);
            if (read.Lines.Count == 0 && !read.Fresh)
            {
                return null;
            }
            int errors = _skippedErrors, warnings = _skippedWarnings;
            var crashed = _skippedCrash;
            foreach (var line in read.Lines)
            {
                Count(line, ref errors, ref warnings, ref crashed);
            }
            return new EditorLogRead(read.Lines, read.Fresh, errors, warnings, crashed, read.Skipped > 0);
        }

        private void Count(string line, ref int errors, ref int warnings, ref bool crashed)
        {
            switch (_source.Classify(line))
            {
                case LogLineLevel.Error:
                    errors++;
                    break;
                case LogLineLevel.Warning:
                    warnings++;
                    break;
            }
            crashed |= _source.IsCrash(line);
        }
    }
}
