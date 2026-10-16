namespace Claudette.Core.Files;

/// <summary>
/// Watches folders for files appearing, going, being renamed or written (DESIGN.md §18, "Project files"): the explorer's
/// folders, and the folders of open files. Behind an interface so tests say what changed rather than wait on the OS.
/// </summary>
public interface IFolderWatcher
{
    /// <summary>
    /// Watches <paramref name="folder"/>'s own entries, not its subfolders'. <paramref name="changed"/> gets the full path
    /// of what changed, or null when the OS lost track and anything might have; it's called on any thread. Disposing
    /// the result stops watching. A folder that can't be watched returns a watch that never calls.
    /// </summary>
    IDisposable Watch(string folder, Action<string?> changed);
}

/// <summary>The OS's own file notifications, through <see cref="FileSystemWatcher"/>.</summary>
public sealed class FolderWatcher : IFolderWatcher
{
    public IDisposable Watch(string folder, Action<string?> changed)
    {
        try
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Changed += (_, e) => changed(e.FullPath);
            watcher.Created += (_, e) => changed(e.FullPath);
            watcher.Deleted += (_, e) => changed(e.FullPath);
            watcher.Renamed += (_, e) =>
            {
                changed(e.OldFullPath);
                changed(e.FullPath);
            };
            watcher.Error += (_, _) => changed(null);
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return NoWatch.Instance;
        }
    }

    private sealed class NoWatch : IDisposable
    {
        public static readonly NoWatch Instance = new();

        public void Dispose()
        {
        }
    }
}
