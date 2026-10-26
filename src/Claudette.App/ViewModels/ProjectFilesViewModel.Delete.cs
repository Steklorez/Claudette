using Claudette.Core.Files;
using Claudette.Core.ProjectFiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// Deleting a file or folder from the explorer, asked first in place, under the explorer's header (DESIGN.md §18,
/// "Project files", "Renaming and deleting"): what goes, where to, and the unsaved edits that go with it.
/// </summary>
public sealed partial class DeleteQuestion : ObservableObject
{
    internal DeleteQuestion(ExplorerRow row, string text, bool isPermanent)
    {
        Row = row;
        Text = text;
        IsPermanent = isPermanent;
    }

    /// <summary>The row being deleted.</summary>
    public ExplorerRow Row { get; }

    public string Path => Row.FullPath;

    /// <summary>The question, in words.</summary>
    public string Text { get; }

    /// <summary>It can't go to the trash, so this deletes it for good.</summary>
    public bool IsPermanent { get; }

    public string ConfirmText => IsPermanent ? "Delete permanently" : "Delete";

    /// <summary>Why the last try didn't delete it, under the question in the error color; null when there's been none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; set; }

    public bool HasProblem => Problem is not null;

    /// <summary>It's being deleted: the button doesn't start it twice.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    public partial bool IsDeleting { get; set; }

    public bool CanConfirm => !IsDeleting;
}

/// <summary>Deleting from the explorer (DESIGN.md §18, "Project files", "Renaming and deleting").</summary>
public sealed partial class ProjectFilesViewModel
{
    /// <summary>How many files a folder's question counts before it says "more than".</summary>
    internal const int FilesCounted = 10_000;

    /// <summary>What's being asked before something is deleted, while it's asked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAskingToDelete))]
    public partial DeleteQuestion? Deleting { get; private set; }

    public bool IsAskingToDelete => Deleting is not null;

    /// <summary>The question shows: the view puts the keyboard on its button.</summary>
    public event Action? DeleteAsked;

    /// <summary>
    /// <b>Delete</b> on a file's or folder's menu, or <c>Delete</c> (<c>Cmd+Backspace</c> on macOS) in the tree: asks first,
    /// in place, saying what goes and where to.
    /// </summary>
    [RelayCommand]
    private async Task DeleteRowAsync(ExplorerRow? row)
    {
        if (row is null or NameRow || row.IsDeleted || Rows.IndexOf(row) < 0)
        {
            return;
        }
        CancelNaming();
        var path = row.FullPath;
        var files = row.IsFolder && !row.IsLink ? await Task.Run(() => DeleteEntry.CountFiles(path, FilesCounted)) : 0;
        Deleting = new DeleteQuestion(row, $"{WhatGoes(row, files)} It goes to {_services.Trash.Name}.{UnsavedNote(path)}", isPermanent: false);
        DeleteAsked?.Invoke();
    }

    /// <summary>What the question names: the file, the folder and how many files are in it, or the link (what it leads to stays).</summary>
    private static string WhatGoes(ExplorerRow row, int files)
    {
        if (row.IsLink)
        {
            return $"Delete the link {row.Name}? What it leads to stays.";
        }
        if (!row.IsFolder)
        {
            return $"Delete {row.Name}?";
        }
        return files switch
        {
            0 => $"Delete the empty folder {row.Name}?",
            1 => $"Delete the folder {row.Name} and the file in it?",
            >= FilesCounted => $"Delete the folder {row.Name} and everything in it, more than {FilesCounted:N0} files?",
            _ => $"Delete the folder {row.Name} and the {files:N0} files in it?",
        };
    }

    /// <summary>The files open from it with unsaved edits, in any Files tab, which go too; empty when there are none.</summary>
    private string UnsavedNote(string path)
    {
        var unsaved = _shell.OpenFilesAtOrUnder(path).Where(f => f.IsModified).Select(f => f.Name).Distinct().ToList();
        return unsaved.Count switch
        {
            0 => "",
            1 => $" {unsaved[0]} has unsaved edits, which go with it.",
            _ => $" {Core.Formats.List(unsaved.Count > 4 ? [.. unsaved.Take(4), $"{unsaved.Count - 4} more"] : unsaved)} have unsaved edits, which go with it.",
        };
    }

    /// <summary>
    /// The question's <b>Delete</b>: to the trash. Where there's none, it asks again, to delete it for good; a failure
    /// says why under the question, which stays. Once it's gone, its open files close in every Files tab, edits and all,
    /// and the row after it is selected.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (Deleting is not { IsDeleting: false } question)
        {
            return;
        }
        question.IsDeleting = true;
        var path = question.Path;
        string? failed;
        if (question.IsPermanent)
        {
            failed = await Task.Run(() => DeleteEntry.DeletePermanently(path));
        }
        else
        {
            var result = await _services.Trash.MoveToTrashAsync(path);
            if (result.Outcome == TrashOutcome.NoTrash)
            {
                Deleting = new DeleteQuestion(question.Row,
                    $"{question.Row.Name} can't go to {_services.Trash.Name}.{Sentence(result.Reason)} Delete it permanently? It can't be put back.{UnsavedNote(path)}",
                    isPermanent: true);
                DeleteAsked?.Invoke();
                return;
            }
            failed = result.Outcome == TrashOutcome.Failed ? $"Claudette couldn't move it to {_services.Trash.Name}.{Sentence(result.Reason)}" : null;
        }
        if (!ReferenceEquals(Deleting, question))
        {
            return;
        }
        if (failed is not null)
        {
            question.Problem = failed;
            question.IsDeleting = false;
            return;
        }
        Deleting = null;
        await DeletedAsync(question.Row);
    }

    /// <summary>A reason, as a sentence after another: a space before it, and a full stop at its end.</summary>
    private static string Sentence(string? reason) =>
        reason is not { Length: > 0 } said ? "" : said.TrimEnd().EndsWith('.') ? $" {said.Trim()}" : $" {said.Trim()}.";

    /// <summary>It's gone: its open files close everywhere, its folder is read again, and the row after it is selected.</summary>
    private async Task DeletedAsync(ExplorerRow row)
    {
        var index = Rows.IndexOf(row);
        var end = index + 1;
        while (end > 0 && end < Rows.Count && Rows[end].Depth > row.Depth)
        {
            end++;
        }
        var after = end > 0 && end < Rows.Count && Rows[end].Depth == row.Depth ? Rows[end] : null;
        var before = index > 0 ? Rows[index - 1] : null;
        _shell.CloseDeleted(row.FullPath);
        _foldersToRead.Add(Path.GetDirectoryName(row.FullPath)!);
        await ReadQueuedFoldersAsync();
        SelectedRow = after is not null && Rows.Contains(after) ? after : before is not null && Rows.Contains(before) ? before : null;
        TreeFocusRequested?.Invoke();
    }

    /// <summary>The question's <b>Cancel</b>, or <c>Esc</c>: nothing goes.</summary>
    [RelayCommand]
    private void CancelDelete()
    {
        if (Deleting is { IsDeleting: false })
        {
            Deleting = null;
            TreeFocusRequested?.Invoke();
        }
    }

    // ---- What a rename or a delete in any Files tab means for this one's open files --------------------------------

    /// <summary>The files open here from <paramref name="path"/>: the file itself, or those in the folder.</summary>
    internal IEnumerable<ProjectFileViewModel> OpenFilesAtOrUnder(string path) => OpenFiles.Where(f => IsAtOrUnder(f.Path, path));

    /// <summary>
    /// <paramref name="oldPath"/> was renamed to <paramref name="newPath"/>: the files open from it follow it, with their
    /// edits, under their new paths.
    /// </summary>
    internal void FollowRename(string oldPath, string newPath)
    {
        var moved = OpenFilesAtOrUnder(oldPath).ToArray();
        foreach (var file in moved)
        {
            var oldFolder = Path.GetDirectoryName(file.Path);
            file.MoveTo(newPath + file.Path[oldPath.Length..]);
            if (Path.GetDirectoryName(file.Path) is { } folder)
            {
                Watch(folder);
            }
            if (oldFolder is not null)
            {
                Unwatch(oldFolder);
            }
            ApplyBaseline(file);
        }
        if (moved.Length > 0)
        {
            UpdateTabLabels();
            // The sidebar's row names the file showing.
            OnPropertyChanged(nameof(RowDetail));
            OnFileStateChanged();
        }
    }

    /// <summary><paramref name="path"/> was deleted, as asked: the files open from it close, their unsaved edits and their copies too.</summary>
    internal void CloseDeleted(string path)
    {
        foreach (var file in OpenFilesAtOrUnder(path).ToArray())
        {
            file.DiscardBackup();
            Remove(file);
        }
    }

    private static bool IsAtOrUnder(string path, string at)
    {
        if (PathComparer.Equals(path, at))
        {
            return true;
        }
        var folder = Path.TrimEndingDirectorySeparator(at) + Path.DirectorySeparatorChar;
        return path.Length > folder.Length && PathComparer.Equals(path[..folder.Length], folder);
    }
}
