namespace Claudette.Core.Perforce;

/// <summary>
/// The Perforce state of the files in the folders asked about, kept between asks (DESIGN.md §18, "Perforce in the Files
/// tab"), so the explorer draws marks from here and never waits for <c>p4</c>. A folder's files are asked about in one
/// <c>p4 fstat</c>, which replaces all of that folder's states at once. The latest ask for a folder wins: one that a
/// later one overtook, finishing after it began, drops its answer (<see cref="Complete"/>). Paths compare as the OS
/// compares them. Thread-safe.
/// </summary>
public sealed class PerforceStatusCache
{
    private readonly Lock _lock = new();
    private readonly StringComparer _comparer;
    private readonly Dictionary<string, Dictionary<string, PerforceFileState>> _folders;
    private readonly Dictionary<string, long> _asks;
    private long _nextAsk;

    public PerforceStatusCache(StringComparer? pathComparer = null)
    {
        _comparer = pathComparer ?? (OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        _folders = new(_comparer);
        _asks = new(_comparer);
    }

    /// <summary>An ask about <paramref name="folder"/>'s files is starting: its number, for <see cref="Complete"/>.</summary>
    public long Begin(string folder)
    {
        lock (_lock)
        {
            var ask = ++_nextAsk;
            _asks[Normalize(folder)] = ask;
            return ask;
        }
    }

    /// <summary>
    /// The answer to ask <paramref name="ask"/> about <paramref name="folder"/>'s files: they replace what the folder had,
    /// each file of it the answer doesn't name being one Perforce doesn't have. False, and nothing changes, when a later
    /// ask began meanwhile.
    /// </summary>
    public bool Complete(string folder, long ask, IEnumerable<PerforceFileState> states)
    {
        var key = Normalize(folder);
        lock (_lock)
        {
            if (!_asks.TryGetValue(key, out var latest) || latest != ask)
            {
                return false;
            }
            // By name in the folder asked about: the workspace's root may be written another way than the folder's path
            // (a link, another case, macOS's /private/var for /var).
            var files = new Dictionary<string, PerforceFileState>(_comparer);
            foreach (var state in states)
            {
                var path = Path.Combine(key, Path.GetFileName(Normalize(state.Path)));
                files[path] = state with { Path = path };
            }
            _folders[key] = files;
            return true;
        }
    }

    /// <summary>One file's state, asked about on its own: kept when its folder's are, else not.</summary>
    public void Update(string path, PerforceFileState state)
    {
        var key = Normalize(path);
        lock (_lock)
        {
            if (_folders.TryGetValue(Parent(key), out var files))
            {
                files[key] = state with { Path = key };
            }
        }
    }

    /// <summary>The ask came to nothing (the server didn't answer): it no longer stands in the way of the next.</summary>
    public void Abandon(string folder, long ask)
    {
        lock (_lock)
        {
            var key = Normalize(folder);
            if (_asks.TryGetValue(key, out var latest) && latest == ask)
            {
                _asks.Remove(key);
            }
        }
    }

    /// <summary>Whether <paramref name="folder"/>'s files have been asked about and answered.</summary>
    public bool Knows(string folder)
    {
        lock (_lock)
        {
            return _folders.ContainsKey(Normalize(folder));
        }
    }

    /// <summary>
    /// <paramref name="path"/>'s state: as answered, or <see cref="PerforceFileStatus.NotInDepot"/> when its folder was
    /// answered without it; null when its folder hasn't been.
    /// </summary>
    public PerforceFileState? Find(string path)
    {
        var key = Normalize(path);
        lock (_lock)
        {
            if (!_folders.TryGetValue(Parent(key), out var files))
            {
                return null;
            }
            return files.TryGetValue(key, out var state) ? state : PerforceFileState.NotInDepotAt(path);
        }
    }

    /// <summary>The folders answered at or under <paramref name="folder"/>, for refreshing after a command on all of it.</summary>
    public IReadOnlyList<string> FoldersAtOrUnder(string folder)
    {
        var top = Normalize(folder);
        var prefix = top + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        lock (_lock)
        {
            return [.. _folders.Keys.Where(f => _comparer.Equals(f, top) || f.StartsWith(prefix, comparison))];
        }
    }

    /// <summary>Forgets every folder's states: Settings stopped showing them, so they'd only go stale.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _folders.Clear();
            _asks.Clear();
        }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(path);

    private static string Parent(string path) => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) ?? "";
}
