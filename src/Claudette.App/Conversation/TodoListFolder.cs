using Claudette.App.Services;
using Claudette.Core.Files;
using Claudette.Core.Sessions;

namespace Claudette.App.Conversation;

/// <summary>
/// Keeps a tab's <see cref="TodoList"/> in step with the list Claude Code keeps in its tasks folder (DESIGN.md §5,
/// "Tasks"), which is the list Claude sees. The folder is read in the background after each answer to one of the Task
/// tools' calls and whenever it changes, as it does when another session shares the list. A folder that turns out not to
/// have this session's list is left alone until the tab follows another; the calls alone keep the list then.
/// </summary>
internal sealed class TodoListFolder : IDisposable
{
    private readonly TodoList _list;
    private readonly IFolderWatcher _watcher;
    private readonly IUiDispatcher _dispatcher;

    /// <summary>The folder the tab follows; null while it follows none.</summary>
    private string? _folder;

    /// <summary>The folder followed didn't have this session's list.</summary>
    private bool _wrongList;

    private IDisposable? _watch;
    private bool _reading;
    private bool _readAgain;
    private bool _disposed;

    public TodoListFolder(TodoList list, IFolderWatcher watcher, IUiDispatcher dispatcher)
    {
        _list = list;
        _watcher = watcher;
        _dispatcher = dispatcher;
        list.Answered += Refresh;
    }

    /// <summary>The folder read, when it has this session's list; null otherwise.</summary>
    public string? Following => _wrongList ? null : _folder;

    /// <summary>Follows <paramref name="folder"/> from now on, and reads it; null follows none.</summary>
    public void Follow(string? folder)
    {
        if (_disposed || folder == _folder)
        {
            return;
        }
        _folder = folder;
        _wrongList = false;
        StopWatching();
        Refresh();
    }

    /// <summary>Reads the folder again in the background. A read asked for during one follows it.</summary>
    public void Refresh()
    {
        if (_disposed || Following is not { } folder)
        {
            return;
        }
        if (_reading)
        {
            _readAgain = true;
            return;
        }
        _reading = true;
        _ = ReadAsync(folder, _list.Version);
    }

    private async Task ReadAsync(string folder, long readAt)
    {
        var (exists, tasks) = await Task.Run(() => (Directory.Exists(folder), ClaudeTaskFolder.Read(folder))).ConfigureAwait(false);
        _dispatcher.Post(() => Finish(folder, readAt, exists, tasks));
    }

    private void Finish(string folder, long readAt, bool exists, IReadOnlyList<ClaudeTask>? tasks)
    {
        _reading = false;
        if (folder == Following && !_disposed)
        {
            if (tasks is not null && !_list.ApplyFolder(tasks, readAt))
            {
                _wrongList = true;
                StopWatching();
                return;
            }
            // Once Claude Code has made it: a folder that isn't there can't be watched.
            if (exists && _watch is null)
            {
                _watch = _watcher.Watch(folder, _ => _dispatcher.Post(Refresh));
            }
        }
        if (_readAgain)
        {
            _readAgain = false;
            Refresh();
        }
    }

    private void StopWatching()
    {
        _watch?.Dispose();
        _watch = null;
        _readAgain = false;
    }

    public void Dispose()
    {
        _disposed = true;
        _list.Answered -= Refresh;
        StopWatching();
    }
}
