using Claudette.Core.ProjectFiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// The explorer's row a name is typed in (DESIGN.md §18, "Project files", "New files and folders" and "Renaming and
/// deleting"): a new file's or folder's, at the top of the folder it goes in, or a new name for one, in its place. It's
/// indented as the entries around it are, and says under the box why a name can't be used as it's typed. It isn't a
/// file or a folder of the tree: it can't be opened or expanded, and its paths end with a separator, as no entry's do,
/// so nothing that looks a row up by its path finds it.
/// </summary>
public abstract partial class NameRow : ExplorerRow
{
    private protected NameRow(TreeEntry entry, int depth, string folder, bool isFolder) : base(entry, depth)
    {
        InFolder = folder;
        IsFolderName = isFolder;
    }

    /// <summary>The folder it's in.</summary>
    public string InFolder { get; }

    /// <summary>It's a folder's name, with a folder's icon; else a file's.</summary>
    public bool IsFolderName { get; }

    public bool IsFileName => !IsFolderName;

    /// <summary>The box's placeholder, and its name for screen readers.</summary>
    public string Placeholder => IsFolderName ? "Folder name" : "File name";

    /// <summary>The name typed.</summary>
    [ObservableProperty]
    public partial string NameText { get; set; } = "";

    partial void OnNameTextChanged(string value) => Problem = Check(value).Problem;

    /// <summary>What the name typed makes: its path, or why it can't be used; neither for nothing to do.</summary>
    internal abstract NewEntryName Check(string typed);

    /// <summary>Why the name typed can't be used, under the box in the error color; null when it can.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; set; }

    public bool HasProblem => Problem is not null;
}

/// <summary>A new file's or folder's name, typed at the top of the folder it goes in.</summary>
public sealed class NewEntryRow : NameRow
{
    internal NewEntryRow(string folder, string relativeFolder, int depth, bool isFolder)
        : base(new TreeEntry("", Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, relativeFolder + "/", IsFolder: false), depth, folder, isFolder)
    {
    }

    /// <summary>It's a folder's name: <b>New folder</b>.</summary>
    public bool IsNewFolder => IsFolderName;

    /// <summary>What a screen reader reads for its row.</summary>
    public override string ToString() => IsNewFolder ? "New folder" : "New file";

    internal override NewEntryName Check(string typed) => NewEntry.Check(InFolder, typed);
}

/// <summary>
/// A file's or folder's new name, typed in its row's place (<b>Rename</b>): its name to start with, with a file's name up
/// to its extension selected, so typing replaces that and keeps the extension.
/// </summary>
public sealed class RenameRow : NameRow
{
    internal RenameRow(ExplorerRow renaming)
        : base(new TreeEntry("", renaming.FullPath + Path.DirectorySeparatorChar, renaming.RelativePath + "/", IsFolder: false), renaming.Depth,
            Path.GetDirectoryName(renaming.FullPath)!, renaming.IsFolder)
    {
        Renaming = renaming;
        NameText = renaming.Name;
    }

    /// <summary>The row being renamed, which comes back if nothing is.</summary>
    public ExplorerRow Renaming { get; }

    /// <summary>How much of the name is selected when the box opens: a file's up to its extension (all of <c>.gitignore</c>), a folder's all.</summary>
    public int SelectedLength => IsFileName && Path.GetFileNameWithoutExtension(Renaming.Name) is { Length: > 0 } stem ? stem.Length : Renaming.Name.Length;

    /// <summary>What a screen reader reads for its row.</summary>
    public override string ToString() => $"Rename {Renaming.Name}";

    internal override NewEntryName Check(string typed) => RenameEntry.Check(Renaming.FullPath, typed);
}

/// <summary>
/// New files and folders made from the explorer, and files and folders renamed there (DESIGN.md §18, "Project files",
/// "New files and folders" and "Renaming and deleting").
/// </summary>
public sealed partial class ProjectFilesViewModel
{
    /// <summary>The row a name is being typed in, a new entry's or a new name; null when there's none.</summary>
    internal NameRow? NamingRow { get; private set; }

    /// <summary>A row waits for its name: the view brings it into view, and its box takes the keyboard.</summary>
    public event Action<NameRow>? NamingStarted;

    /// <summary>A new folder was made and selected, or something renamed or deleted: the keyboard goes back to the tree.</summary>
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
        CancelNaming();
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
        CancelNaming();
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
        var index = at is null or NameRow ? -1 : Rows.IndexOf(at);
        if (index < 0)
        {
            return null;
        }
        return at!.CanExpand ? at : Rows.Take(index).LastOrDefault(r => r.Depth < at.Depth);
    }

    /// <summary>
    /// <b>Rename</b> on a file's or folder's menu, or <c>F2</c> in the tree: a box takes the row's place, with its name in it,
    /// a file's up to its extension selected.
    /// </summary>
    [RelayCommand]
    private void RenameRow(ExplorerRow? row)
    {
        if (row is null or NameRow || row.IsDeleted || Rows.IndexOf(row) < 0)
        {
            return;
        }
        CancelNaming();
        var renaming = new RenameRow(row);
        Rows[Rows.IndexOf(row)] = renaming;
        NamingRow = renaming;
        NamingStarted?.Invoke(renaming);
    }

    /// <summary>
    /// <c>Enter</c> in a name's box: the new file or folder is made, or the row renamed, and selected in the tree. A name
    /// that can't be used keeps the box, saying why; an empty one, or a name as it was, does nothing.
    /// </summary>
    [RelayCommand]
    private async Task CommitNamingAsync()
    {
        if (NamingRow is not { } row)
        {
            return;
        }
        var name = row.Check(row.NameText);
        if (name.Path is not { } path)
        {
            if (name.Problem is null)
            {
                CancelNaming();
            }
            else
            {
                row.Problem = name.Problem;
            }
            return;
        }
        if (row is RenameRow rename)
        {
            await RenameAsync(rename, path);
            return;
        }
        var made = (NewEntryRow)row;
        if (NewEntry.Make(path, made.IsNewFolder) is { } failed)
        {
            row.Problem = failed;
            return;
        }
        EndNaming(row);
        _foldersToRead.Add(row.InFolder);
        await ReadQueuedFoldersAsync();
        await RevealInTreeAsync(path);
        if (made.IsNewFolder)
        {
            TreeFocusRequested?.Invoke();
        }
        else
        {
            await OpenFileAsync(path, preview: false, focus: true);
        }
    }

    /// <summary>
    /// Renames the row's file or folder to <paramref name="path"/>, never over anything there. The files open from it follow
    /// it, in every Files tab, edits and all; a folder that was expanded stays so, with the folders in it that were.
    /// </summary>
    private async Task RenameAsync(RenameRow row, string path)
    {
        var old = row.Renaming.FullPath;
        if (RenameEntry.Rename(old, path) is { } failed)
        {
            row.Problem = failed;
            return;
        }
        // The row back in its place, with the rows under it, says what was expanded.
        EndNaming(row);
        var expanded = ExpandedAtOrUnder(row.Renaming).Select(folder => path + folder[old.Length..]).ToArray();
        _shell.FollowRename(old, path);
        _foldersToRead.Add(row.InFolder);
        await ReadQueuedFoldersAsync();
        // Shallowest first, so each folder's parent is there to expand it in.
        foreach (var folder in expanded.OrderBy(f => f.Length))
        {
            if (Rows.FirstOrDefault(r => r.IsFolder && PathComparer.Equals(r.FullPath, folder)) is { } again)
            {
                await ExpandAsync(again);
            }
        }
        await RevealInTreeAsync(path);
        TreeFocusRequested?.Invoke();
    }

    /// <summary>The paths of <paramref name="row"/>, if it's expanded, and of the expanded folders under it.</summary>
    private List<string> ExpandedAtOrUnder(ExplorerRow row)
    {
        var expanded = new List<string>();
        var index = Rows.IndexOf(row);
        if (!row.IsExpanded || index < 0)
        {
            return expanded;
        }
        expanded.Add(row.FullPath);
        for (var i = index + 1; i < Rows.Count && Rows[i].Depth > row.Depth; i++)
        {
            if (Rows[i].IsExpanded)
            {
                expanded.Add(Rows[i].FullPath);
            }
        }
        return expanded;
    }

    /// <summary><c>Esc</c> in a name's box, or a click elsewhere: nothing is made or renamed.</summary>
    [RelayCommand]
    internal void CancelNaming()
    {
        if (NamingRow is not { } row)
        {
            return;
        }
        EndNaming(row);
        if (row is RenameRow rename && Rows.Contains(rename.Renaming))
        {
            SelectedRow = rename.Renaming;
        }
        // What changed on disk while the name was typed shows now.
        if (_foldersToRead.Count > 0)
        {
            _ = ReadQueuedFoldersAsync();
        }
    }

    /// <summary>The box goes: a new entry's row with it, a renamed row back in its place.</summary>
    private void EndNaming(NameRow row)
    {
        // Before the row goes, since its box losing the keyboard leaves it too.
        NamingRow = null;
        var index = Rows.IndexOf(row);
        if (index < 0)
        {
            return;
        }
        if (row is RenameRow rename)
        {
            Rows[index] = rename.Renaming;
        }
        else
        {
            Rows.RemoveAt(index);
        }
    }

    /// <summary>The name's row went with its folder, collapsed or gone from disk: there's no name to wait for.</summary>
    private void ForgetNamingRowIfGone()
    {
        if (NamingRow is { } row && !Rows.Contains(row))
        {
            NamingRow = null;
        }
    }
}
