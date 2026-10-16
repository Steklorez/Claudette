using Claudette.Core.Files;

namespace Claudette.App.Tests.Support;

/// <summary>
/// Stands in for the OS's file notifications (DESIGN.md §18, "Project files"): which folders are watched, and a test says
/// when something in one changed rather than waiting on the OS.
/// </summary>
internal sealed class FakeFolderWatcher : IFolderWatcher
{
    private readonly Lock _lock = new();
    private readonly List<(string Folder, Action<string?> Changed)> _watches = [];

    /// <summary>The folders watched now.</summary>
    public IReadOnlyList<string> Folders
    {
        get
        {
            lock (_lock)
            {
                return [.. _watches.Select(w => w.Folder)];
            }
        }
    }

    public IDisposable Watch(string folder, Action<string?> changed)
    {
        var watch = (folder, changed);
        lock (_lock)
        {
            _watches.Add(watch);
        }
        return new Unwatch(() =>
        {
            lock (_lock)
            {
                _watches.Remove(watch);
            }
        });
    }

    /// <summary>Says <paramref name="path"/> changed, to whoever watches its folder.</summary>
    public void Changed(string path)
    {
        var folder = Path.GetDirectoryName(path) ?? "";
        List<Action<string?>> listeners;
        lock (_lock)
        {
            listeners = [.. _watches.Where(w => string.Equals(Path.TrimEndingDirectorySeparator(w.Folder), Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase)).Select(w => w.Changed)];
        }
        foreach (var listener in listeners)
        {
            listener(path);
        }
    }

    private sealed class Unwatch(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
