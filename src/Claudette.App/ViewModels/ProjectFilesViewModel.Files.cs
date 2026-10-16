using System.Collections.ObjectModel;
using Claudette.Core.Protocol;
using Claudette.Core.ProjectFiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// Closing files with unsaved edits (DESIGN.md §18, "Project files", "Saving"): asked in place, once for all of them.
/// </summary>
public sealed partial class CloseQuestion : ObservableObject
{
    private readonly TaskCompletionSource<bool> _answer = new();

    internal CloseQuestion(IReadOnlyList<ProjectFileViewModel> unsaved, bool closesTab)
    {
        Unsaved = unsaved;
        ClosesTab = closesTab;
    }

    /// <summary>The files with unsaved edits among those closing.</summary>
    public IReadOnlyList<ProjectFileViewModel> Unsaved { get; }

    /// <summary>It's the Project Files tab closing, or its group.</summary>
    public bool ClosesTab { get; }

    public string Text => Unsaved.Count == 1
        ? $"Save {Unsaved[0].Name}?"
        : $"Save {Unsaved.Count} files? {string.Join(", ", Unsaved.Take(4).Select(f => f.Name))}{(Unsaved.Count > 4 ? $" and {Unsaved.Count - 4} more" : "")} have unsaved edits.";

    public string SaveText => Unsaved.Count == 1 ? "Save" : "Save all";

    /// <summary>True when the files can close: saved, or thrown away. False for Cancel, or a save that didn't happen.</summary>
    internal Task<bool> Answer => _answer.Task;

    internal void Settle(bool close) => _answer.TrySetResult(close);
}

/// <summary>The files open in the Project Files tab, and its file tabs (DESIGN.md §18, "Project files", "File tabs").</summary>
public sealed partial class ProjectFilesViewModel
{
    public ObservableCollection<ProjectFileViewModel> OpenFiles { get; } = [];

    /// <summary>The file showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowDetail), nameof(HasShowing))]
    public partial ProjectFileViewModel? Showing { get; set; }

    public bool HasShowing => Showing is not null;

    partial void OnShowingChanged(ProjectFileViewModel? oldValue, ProjectFileViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsShowing = false;
        }
        if (newValue is not null)
        {
            newValue.IsShowing = true;
        }
        OnFileStateChanged();
    }

    /// <summary>What's being asked before files close, while it's asked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAskingToClose))]
    public partial CloseQuestion? Closing { get; private set; }

    public bool IsAskingToClose => Closing is not null;

    /// <summary>
    /// Opens <paramref name="path"/>, or shows it if it's open: in the preview tab, which the next file opened that way
    /// replaces, or to stay. Goes to <paramref name="line"/> (and selects through <paramref name="endLine"/>) when given.
    /// </summary>
    public async Task<ProjectFileViewModel> OpenFileAsync(string path, bool preview, int? line = null, int? column = null, int? endLine = null, bool focus = false)
    {
        var file = OpenFiles.FirstOrDefault(f => PathComparer.Equals(f.Path, path));
        if (file is not null)
        {
            if (!preview)
            {
                file.IsPreview = false;
            }
        }
        else
        {
            file = new ProjectFileViewModel(_services, this, new OpenFileState { Path = path, IsPreview = preview });
            var replaced = preview ? OpenFiles.FirstOrDefault(f => f.IsPreview && !f.IsModified) : null;
            if (replaced is not null)
            {
                OpenFiles[OpenFiles.IndexOf(replaced)] = file;
                replaced.Dispose();
            }
            else
            {
                var after = Showing is null ? OpenFiles.Count - 1 : OpenFiles.IndexOf(Showing);
                OpenFiles.Insert(Math.Max(after + 1, OpenFiles.Count(f => f.IsPinned)), file);
            }
            UpdateTabLabels();
            if (Path.GetDirectoryName(path) is { } folder)
            {
                Watch(folder);
            }
        }
        Showing = file;
        if (!file.IsLoaded)
        {
            if (_started)
            {
                await file.LoadAsync();
            }
            else
            {
                Start();
            }
        }
        if (line is { } at)
        {
            file.Reveal(at, column ?? 1, endLine);
        }
        if (focus)
        {
            file.RequestFocus();
        }
        OnFileStateChanged();
        return file;
    }

    /// <summary>A file in no folder this project shows, Claude deleted: opening it shows its diff.</summary>
    private Task OpenDeletedAsync(ExplorerRow row) => OpenDiffAsync(row.FullPath);

    /// <summary><b>Open diff</b>: the file, side by side with what it held before.</summary>
    private async Task OpenDiffAsync(string path)
    {
        var file = await OpenFileAsync(path, preview: false);
        if (file.IsText)
        {
            file.Mode = Core.ProjectFiles.FileViewMode.SideBySide;
        }
    }

    /// <summary>A click on a file's tab.</summary>
    [RelayCommand]
    private void ShowFile(ProjectFileViewModel? file)
    {
        if (file is not null)
        {
            Showing = file;
        }
    }

    /// <summary>Double-clicking a tab keeps a preview.</summary>
    [RelayCommand]
    private void KeepFile(ProjectFileViewModel? file)
    {
        if (file is not null)
        {
            file.IsPreview = false;
            OnFileStateChanged();
        }
    }

    // ---- Closing -------------------------------------------------------------------------------------------------

    /// <summary>A tab's ×, its middle click, or <b>Close</b>: asks first, in place, if it has unsaved edits.</summary>
    [RelayCommand]
    private Task CloseFileAsync(ProjectFileViewModel? file) => file is null ? Task.CompletedTask : CloseFilesAsync([file]);

    [RelayCommand]
    private Task CloseOtherFilesAsync(ProjectFileViewModel? file) =>
        CloseFilesAsync([.. OpenFiles.Where(f => !ReferenceEquals(f, file) && !f.IsPinned)]);

    [RelayCommand]
    private Task CloseFilesToTheRightAsync(ProjectFileViewModel? file) =>
        file is null ? Task.CompletedTask : CloseFilesAsync([.. OpenFiles.Skip(OpenFiles.IndexOf(file) + 1).Where(f => !f.IsPinned)]);

    [RelayCommand]
    private Task CloseSavedFilesAsync() => CloseFilesAsync([.. OpenFiles.Where(f => !f.IsModified && !f.IsPinned)]);

    [RelayCommand]
    private Task CloseAllFilesAsync() => CloseFilesAsync([.. OpenFiles.Where(f => !f.IsPinned)]);

    /// <summary>
    /// <c>Ctrl/Cmd+W</c> with the Project Files tab focused: the file showing, and once none is open, the tab itself.
    /// </summary>
    internal Task CloseShowingOrTabAsync() => Showing is { } showing ? CloseFilesAsync([showing]) : _shell.CloseProjectFilesAsync(this);

    /// <summary>Closes <paramref name="files"/>, asking once about the ones with unsaved edits. False when it didn't.</summary>
    private async Task<bool> CloseFilesAsync(IReadOnlyList<ProjectFileViewModel> files)
    {
        if (files.Count == 0)
        {
            return true;
        }
        var unsaved = files.Where(f => f.IsModified).ToList();
        if (unsaved.Count > 0 && !await AskAsync(new CloseQuestion(unsaved, closesTab: false)))
        {
            return false;
        }
        foreach (var file in files)
        {
            Remove(file);
        }
        return true;
    }

    /// <summary>
    /// The Project Files tab, or its group, is closing: asks once about the files with unsaved edits. False for Cancel,
    /// or a save that didn't happen.
    /// </summary>
    internal async Task<bool> ConfirmCloseAsync()
    {
        var unsaved = OpenFiles.Where(f => f.IsModified).ToList();
        return unsaved.Count == 0 || await AskAsync(new CloseQuestion(unsaved, closesTab: true));
    }

    private async Task<bool> AskAsync(CloseQuestion question)
    {
        Closing?.Settle(false);
        Closing = question;
        // On screen, so it can be answered.
        if (!IsShown)
        {
            _shell.Selected = this;
        }
        try
        {
            return await question.Answer;
        }
        finally
        {
            if (ReferenceEquals(Closing, question))
            {
                Closing = null;
            }
        }
    }

    /// <summary>The question's <b>Save</b> (or <b>Save all</b>): they close once every one of them is saved.</summary>
    [RelayCommand]
    private async Task SaveAndCloseAsync()
    {
        if (Closing is not { } question)
        {
            return;
        }
        foreach (var file in question.Unsaved)
        {
            if (!await file.SaveAsync())
            {
                Showing = file;
                question.Settle(false);
                return;
            }
        }
        question.Settle(true);
    }

    /// <summary>The question's <b>Don't save</b>: the edits are thrown away, their copies too.</summary>
    [RelayCommand]
    private void CloseWithoutSaving()
    {
        if (Closing is not { } question)
        {
            return;
        }
        foreach (var file in question.Unsaved)
        {
            file.DiscardBackup();
        }
        question.Settle(true);
    }

    [RelayCommand]
    private void CancelClose() => Closing?.Settle(false);

    private void Remove(ProjectFileViewModel file)
    {
        var index = OpenFiles.IndexOf(file);
        if (index < 0)
        {
            return;
        }
        OpenFiles.RemoveAt(index);
        file.Dispose();
        if (ReferenceEquals(Showing, file))
        {
            Showing = OpenFiles.Count == 0 ? null : OpenFiles[Math.Min(index, OpenFiles.Count - 1)];
        }
        if (Path.GetDirectoryName(file.Path) is { } folder)
        {
            Unwatch(folder);
        }
        UpdateTabLabels();
        OnPropertyChanged(nameof(UnsavedCount));
        OnPropertyChanged(nameof(HasUnsaved));
        OnPropertyChanged(nameof(RowDetail));
        OnFileStateChanged();
    }

    // ---- Pinning and moving --------------------------------------------------------------------------------------

    /// <summary><b>Pin</b> and <b>Unpin</b>: pinned tabs sit first.</summary>
    [RelayCommand]
    private void TogglePinFile(ProjectFileViewModel? file)
    {
        if (file is null)
        {
            return;
        }
        file.IsPinned = !file.IsPinned;
        file.IsPreview = false;
        // Pinned, it goes last among the pinned; unpinned, first after them.
        OpenFiles.Remove(file);
        OpenFiles.Insert(OpenFiles.Count(f => f.IsPinned), file);
        Showing = file;
        OnFileStateChanged();
    }

    /// <summary>Dragging a tab along the row: pinned tabs stay ahead of the others.</summary>
    public bool MoveFile(ProjectFileViewModel file, int index)
    {
        var pinned = OpenFiles.Count(f => f.IsPinned);
        var (first, last) = file.IsPinned ? (0, pinned - 1) : (pinned, OpenFiles.Count - 1);
        var from = OpenFiles.IndexOf(file);
        var to = Math.Clamp(index, first, last);
        if (from < 0 || from == to)
        {
            return false;
        }
        OpenFiles.Move(from, to);
        OnFileStateChanged();
        return true;
    }

    /// <summary><c>Ctrl+PageDown</c>: the next file, going round.</summary>
    [RelayCommand]
    internal void NextFile() => ShowOffset(1);

    /// <summary><c>Ctrl+PageUp</c>: the file before, going round.</summary>
    [RelayCommand]
    internal void PreviousFile() => ShowOffset(-1);

    private void ShowOffset(int offset)
    {
        if (OpenFiles.Count == 0)
        {
            return;
        }
        var at = Showing is null ? 0 : OpenFiles.IndexOf(Showing);
        Showing = OpenFiles[(at + offset + OpenFiles.Count) % OpenFiles.Count];
    }

    // ---- Saving --------------------------------------------------------------------------------------------------

    /// <summary><c>Ctrl/Cmd+S</c>: the file showing.</summary>
    [RelayCommand]
    internal Task SaveAsync() => Showing is { } file ? file.SaveAsync() : Task.CompletedTask;

    /// <summary><c>Ctrl/Cmd+Alt+S</c>: every open file with unsaved edits.</summary>
    [RelayCommand]
    internal async Task SaveAllAsync()
    {
        foreach (var file in OpenFiles.Where(f => f.IsModified).ToArray())
        {
            await file.SaveAsync();
        }
    }

    // ---- What the files tell the tab -------------------------------------------------------------------------------

    internal void OnFileLoaded(ProjectFileViewModel file)
    {
        ApplyBaseline(file);
        OnPropertyChanged(nameof(UnsavedCount));
        OnPropertyChanged(nameof(HasUnsaved));
        OnPropertyChanged(nameof(RowDetail));
    }

    internal void OnFileModifiedChanged(ProjectFileViewModel file)
    {
        OnPropertyChanged(nameof(UnsavedCount));
        OnPropertyChanged(nameof(HasUnsaved));
        OnPropertyChanged(nameof(RowDetail));
        if (file.IsModified)
        {
            file.IsPreview = false;
        }
    }

    internal void OnFileSaved(ProjectFileViewModel file) => OnFileModifiedChanged(file);

    /// <summary>Every open file looked at on disk again: the tab shows, Claudette comes to the front, or a turn ended.</summary>
    internal async Task CheckOpenFilesOnDiskAsync()
    {
        foreach (var file in OpenFiles.ToArray())
        {
            await file.CheckDiskAsync();
        }
    }

    /// <summary>Two open files with the same name show their parent folders too: <c>Player/Config.h</c>, <c>Enemy/Config.h</c>.</summary>
    private void UpdateTabLabels()
    {
        foreach (var file in OpenFiles)
        {
            var clash = OpenFiles.Any(f => !ReferenceEquals(f, file) && string.Equals(f.Name, file.Name, StringComparison.OrdinalIgnoreCase));
            var parent = Path.GetFileName(Path.GetDirectoryName(file.Path) ?? "");
            file.TabLabel = clash && parent.Length > 0 ? $"{parent}/{file.Name}" : file.Name;
        }
    }

    // ---- The file's menu -------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Open in the OS's app</b>. A program or shortcut that isn't text shows in its folder instead, since opening one
    /// runs it (DESIGN.md §18, "Files named in Claude's replies").
    /// </summary>
    internal Task OpenInSystemAsync(string path)
    {
        if (ReplyFileReference.IsProgram(path))
        {
            return RevealAsync(path);
        }
        return File.Exists(path) ? _services.Platform.OpenFileAsync(path) : Task.CompletedTask;
    }

    internal Task RevealAsync(string path) =>
        Path.GetDirectoryName(path) is { } folder && Directory.Exists(folder) ? _services.Platform.RevealFolderAsync(Directory.Exists(path) ? path : folder) : Task.CompletedTask;

    internal Task CopyPathAsync(string path) => _services.Platform.SetClipboardTextAsync(path);

    [RelayCommand]
    private Task OpenFileInSystemAsync(ProjectFileViewModel? file) => file is null ? Task.CompletedTask : OpenInSystemAsync(file.Path);

    [RelayCommand]
    private Task RevealFileAsync(ProjectFileViewModel? file) => file is null ? Task.CompletedTask : RevealAsync(file.Path);

    [RelayCommand]
    private Task CopyFilePathAsync(ProjectFileViewModel? file) => file is null ? Task.CompletedTask : CopyPathAsync(file.Path);

    [RelayCommand]
    private Task CopyFileRelativePathAsync(ProjectFileViewModel? file) =>
        file is null ? Task.CompletedTask : _services.Platform.SetClipboardTextAsync(file.RelativePath);

    [RelayCommand]
    private void ToggleFileReviewed(ProjectFileViewModel? file)
    {
        if (file is not null)
        {
            SetReviewed(file, !file.IsReviewed);
        }
    }
}
