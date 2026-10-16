using System.Collections.Concurrent;
using Claudette.Core.Files;
using Claudette.Core.Tests.Support;

namespace Claudette.Core.Tests.Files;

/// <summary>The OS's notifications for the folders the Project Files tab shows (DESIGN.md §18, "Project files").</summary>
public sealed class FolderWatcherTests : IDisposable
{
    private readonly TempFolder _temp = new("claudette-watcher");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task A_file_appearing_in_a_watched_folder_is_reported()
    {
        var changed = new ConcurrentQueue<string?>();
        using var watch = new FolderWatcher().Watch(_temp.Path, changed.Enqueue);

        _temp.Write("new.txt", "hello");

        // By name: macOS reports /var's files under /private/var.
        await Waiting.UntilAsync(() => changed.Any(p => p is not null && Path.GetFileName(p) == "new.txt"), "the new file to be reported");
    }

    [Fact]
    public void A_folder_that_isnt_there_gives_a_watch_that_does_nothing()
    {
        var watch = new FolderWatcher().Watch(_temp.Combine("missing"), _ => Assert.Fail("A missing folder has nothing to report."));

        Assert.IsNotType<FileSystemWatcher>(watch);
        watch.Dispose();
    }
}
