namespace Deventry.Core.Perforce;

/// <summary>
/// Which of the explorer's entries Perforce's P4IGNORE rules ignore, as <c>p4 ignores -i</c> answered (DESIGN.md §18,
/// "The explorer"), kept so the explorer marks them from here, never waits for <c>p4</c>, and asks about each once. When a
/// P4IGNORE file may have changed, <see cref="Forget"/> has every path asked about again, while what was known still
/// marks it until the new answer comes, so nothing flickers. An answer from before the last forget is dropped. Paths
/// compare as the OS compares them. Thread-safe.
/// </summary>
public sealed class PerforceIgnoreCache
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, bool> _known;
    private readonly Dictionary<string, bool> _stale;
    private long _generation;

    public PerforceIgnoreCache(StringComparer? pathComparer = null)
    {
        var comparer = pathComparer ?? (OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        _known = new(comparer);
        _stale = new(comparer);
    }

    /// <summary>Counts the forgets: an ask takes it as it starts, and its answer is kept only while it's the same.</summary>
    public long Generation
    {
        get
        {
            lock (_lock)
            {
                return _generation;
            }
        }
    }

    /// <summary>Whether <paramref name="path"/> is ignored: as last answered, or before a forget; null when it's never been asked about.</summary>
    public bool? IsIgnored(string path)
    {
        var key = Normalize(path);
        lock (_lock)
        {
            return _known.TryGetValue(key, out var ignored) || _stale.TryGetValue(key, out ignored) ? ignored : null;
        }
    }

    /// <summary>Whether <paramref name="path"/> has been answered since the last forget, so it needn't be asked about.</summary>
    public bool Knows(string path)
    {
        lock (_lock)
        {
            return _known.ContainsKey(Normalize(path));
        }
    }

    /// <summary>
    /// The answers to an ask that started at <paramref name="generation"/>: kept, and true when one of them changed what's
    /// shown. False, and nothing is kept, when a forget came since.
    /// </summary>
    public bool Complete(long generation, IEnumerable<(string Path, bool Ignored)> answers)
    {
        lock (_lock)
        {
            if (generation != _generation)
            {
                return false;
            }
            var changed = false;
            foreach (var (path, ignored) in answers)
            {
                var key = Normalize(path);
                var shown = (_known.TryGetValue(key, out var was) || _stale.TryGetValue(key, out was)) && was;
                changed |= shown != ignored;
                _known[key] = ignored;
                _stale.Remove(key);
            }
            return changed;
        }
    }

    /// <summary>A P4IGNORE file may have changed, or the explorer's <b>Refresh</b>: everything is asked about again.</summary>
    public void Forget()
    {
        lock (_lock)
        {
            foreach (var (path, ignored) in _known)
            {
                _stale[path] = ignored;
            }
            _known.Clear();
            _generation++;
        }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(path);
}
