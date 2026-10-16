using Claudette.Core.ProjectFiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// The explorer's row a new file's or folder's name is typed in (DESIGN.md §18, "Project files", "New files and
/// folders"): at the top of the folder it goes in, indented as its entries are, saying under the box why a name can't be
/// used as it's typed. It isn't a file or a folder of the tree: it can't be opened or expanded, and its paths end with a
/// separator, as no entry's do, so nothing that looks a row up by its path finds it.
/// </summary>
public sealed partial class NewEntryRow : ExplorerRow
{
    internal NewEntryRow(string folder, string relativeFolder, int depth, bool isFolder)
        : base(new TreeEntry("", Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, relativeFolder + "/", IsFolder: false), depth)
    {
        InFolder = folder;
        IsNewFolder = isFolder;
    }

    /// <summary>The folder it's made in.</summary>
    public string InFolder { get; }

    /// <summary>It's a folder's name: <b>New folder</b>.</summary>
    public bool IsNewFolder { get; }

    public bool IsNewFile => !IsNewFolder;

    /// <summary>The box's placeholder, and its name for screen readers.</summary>
    public string Placeholder => IsNewFolder ? "Folder name" : "File name";

    /// <summary>The name typed.</summary>
    [ObservableProperty]
    public partial string NameText { get; set; } = "";

    partial void OnNameTextChanged(string value) => Problem = NewEntry.Check(InFolder, value).Problem;

    /// <summary>Why the name typed can't be used, under the box in the error color; null when it can.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; set; }

    public bool HasProblem => Problem is not null;
}

/// <summary>New files and folders made from the explorer (DESIGN.md §18, "Project files", "New files and folders").</summary>
public sealed partial class ProjectFilesViewModel
{
    /// <summary>The row a new file's or folder's name is being typed in; null when there's none.</summary>
    internal NewEntryRow? NamingRow { get; private set; }

    /// <summary>A new row waits for its name: the view brings it into view, and its box takes the keyboard.</summary>
    public event Action<NewEntryRow>? NamingStarted;

    /// <summary>A new folder was made and selected: the keyboard goes back to the tree.</summary>
    public event Action? TreeFocusRequested;

    /// <summary>
    /// <b>New file</b>: on a folder's menu, in it; on a file's, beside it; from the explorer's header, in the selected row's
    /// folder; on the empty space under the tree, or with nothing selected, at the top of the project.
    /// </summary>
    [RelayCommand]
    private Task NewFileAsync(ExplorerRow? at) => StartNamingAsync(at, isFolder: false);

    /// <summary><b>New folder</b>, from the same places as <b>New file</b>.</summary>
    [RelayCommand]
    private Task NewFolderAsync(ExplorerRow? at) => StartNamingAsync(at, isFolder: true);

    /// <summary>The tree comes back if the found files or the changed ones replaced it, the folder opens, and the new row takes the top of it.</summary>
    private async Task StartNamingAsync(ExplorerRow? at, bool isFolder)
    {
        var folder = FolderRowFor(at);
        CancelNewEntry();
        if (IsFinding)
        {
            StopFind();
        }
        ShowOnlyChanged = false;
        ShowExplorer();
        Start();
        await _explorerLoaded.Task;
        if (folder is not null)
        {
            await ExpandAsync(folder);
            // It went from disk, or was collapsed again, while it was read.
            if (!folder.IsExpanded || Rows.IndexOf(folder) < 0)
            {
                return;
            }
        }
        // The other command's row, started while this folder was read, gives way.
        CancelNewEntry();
        var row = new NewEntryRow(folder?.FullPath ?? Folder, folder?.RelativePath ?? "", folder is null ? 0 : folder.Depth + 1, isFolder);
        Rows.Insert(folder is null ? 0 : Rows.IndexOf(folder) + 1, row);
        NamingRow = row;
        NamingStarted?.Invoke(row);
    }

    /// <summary>
    /// The folder's row a new entry goes in: a folder's own; for a file, or a link that leads back up, the one it's in. Null
    /// for the project's top folder.
    /// </summary>
    private ExplorerRow? FolderRowFor(ExplorerRow? at)
    {
        var index = at is null or NewEntryRow ? -1 : Rows.IndexOf(at);
        if (index < 0)
        {
            return null;
        }
        return at!.CanExpand ? at : Rows.Take(index).LastOrDefault(r => r.Depth < at.Depth);
    }

    /// <summary>
    /// <c>Enter</c> in the new row's box: the file or folder is made and selected in the tree, and a file opens to stay with
    /// the keyboard in it. A name that can't be used keeps the box, saying why; an empty one makes nothing.
    /// </summary>
    [RelayCommand]
    private async Task CommitNewEntryAsync()
    {
        if (NamingRow is not { } row)
        {
            return;
        }
        var name = NewEntry.Check(row.InFolder, row.NameText);
        if (name.Path is not { } path)
        {
            if (name.Problem is null)
            {
                CancelNewEntry();
            }
            else
            {
                row.Problem = name.Problem;
            }
            return;
        }
        if (NewEntry.Make(path, row.IsNewFolder) is { } failed)
        {
            row.Problem = failed;
            return;
        }
        EndNaming(row);
        _foldersToRead.Add(row.InFolder);
        await ReadQueuedFoldersAsync();
        await RevealInTreeAsync(path);
        if (row.IsNewFolder)
        {
            TreeFocusRequested?.Invoke();
        }
        else
        {
            await OpenFileAsync(path, preview: false, focus: true);
        }
    }

    /// <summary><c>Esc</c> in the new row's box, or a click elsewhere: nothing is made.</summary>
    [RelayCommand]
    internal void CancelNewEntry()
    {
        if (NamingRow is not { } row)
        {
            return;
        }
        EndNaming(row);
        // What changed on disk while the name was typed shows now.
        if (_foldersToRead.Count > 0)
        {
            _ = ReadQueuedFoldersAsync();
        }
    }

    private void EndNaming(NewEntryRow row)
    {
        // Before the row goes, since its box losing the keyboard leaves it too.
        NamingRow = null;
        Rows.Remove(row);
    }

    /// <summary>The new row went with its folder, collapsed or gone from disk: there's no name to wait for.</summary>
    private void ForgetNamingRowIfGone()
    {
        if (NamingRow is { } row && !Rows.Contains(row))
        {
            NamingRow = null;
        }
    }
}
