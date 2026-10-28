using Claudette.App.Diffs;
using Claudette.App.Services;
using Claudette.Core.Diffs;
using Claudette.Core.Perforce;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// A Perforce command the explorer asks about first, in place under its header, as it asks before deleting (DESIGN.md
/// §18, "Perforce in the Files tab"): <b>Mark for delete</b>, and <b>Revert</b> of something with changes.
/// </summary>
public sealed partial class PerforceQuestion : ObservableObject
{
    internal PerforceQuestion(string text, string confirmText, Func<Task> confirm)
    {
        Text = text;
        ConfirmText = confirmText;
        Confirm = confirm;
    }

    /// <summary>The question, in words.</summary>
    public string Text { get; }

    /// <summary>Its button: <b>Mark for delete</b>, <b>Revert</b>.</summary>
    public string ConfirmText { get; }

    internal Func<Task> Confirm { get; }

    /// <summary>It's being done: the button doesn't start it twice.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    public partial bool IsRunning { get; set; }

    public bool CanConfirm => !IsRunning;
}

/// <summary>Which of the Perforce menu's items apply to an entry, by what Perforce says of it (DESIGN.md §18, "The Perforce menu").</summary>
internal static class PerforceMenu
{
    /// <summary><b>Check out</b>: a depot file that isn't open, or a folder; when the state isn't known, Perforce says.</summary>
    public static bool CanCheckOut(PerforceFileState? state, bool isFolder) => isFolder || state is null or { Status: PerforceFileStatus.InDepot };

    /// <summary><b>Mark for add</b>: a file Perforce doesn't have, or a folder's new files.</summary>
    public static bool CanMarkForAdd(PerforceFileState? state, bool isFolder) => isFolder || state is null or { Status: PerforceFileStatus.NotInDepot };

    /// <summary><b>Mark for delete</b>: a depot file that isn't open, or a folder's.</summary>
    public static bool CanMarkForDelete(PerforceFileState? state, bool isFolder) => isFolder || state is null or { Status: PerforceFileStatus.InDepot };

    /// <summary><b>Revert</b>: a file open for anything, or a folder's.</summary>
    public static bool CanRevert(PerforceFileState? state, bool isFolder) => isFolder || state is null || state.IsOpen;

    /// <summary><b>Diff against the depot</b>: a file with a revision synced here.</summary>
    public static bool CanDiff(PerforceFileState? state, bool isFolder) => !isFolder && (state is null || state.HasDepotRevision);
}

/// <summary>The word beside a file's name for its Perforce state (DESIGN.md §18, "The state in the tree").</summary>
internal static class PerforceMarks
{
    /// <summary>
    /// What it's opened for here, in Perforce's own words, as <c>p4 opened</c> says them: <c>edit</c>, <c>add</c>,
    /// <c>delete</c>, <c>move</c>, or the action, such as <c>integrate</c>; <c>others</c> for a file only someone else has
    /// open. Null for a file nobody has open, and for one Perforce doesn't have, which would be noise.
    /// </summary>
    public static string? Mark(PerforceFileState? state) => state?.Status switch
    {
        PerforceFileStatus.CheckedOut => "edit",
        PerforceFileStatus.MarkedForAdd => "add",
        PerforceFileStatus.MarkedForDelete => "delete",
        PerforceFileStatus.MovedHere => "move",
        PerforceFileStatus.MovedAway => "moved",
        PerforceFileStatus.Opened => state.Action,
        PerforceFileStatus.InDepot when state.IsOpenedByOthers => "others",
        _ => null,
    };
}

/// <summary>
/// Perforce in the Files tab (DESIGN.md §18, "Perforce in the Files tab"): the workspace, found in the background; each
/// file's state beside its name, from the cache and never waited for; the explorer's and file tabs' Perforce menus; and
/// the automatic actions as files are made, edited, renamed and deleted here. Whatever <c>p4</c> refuses is said under
/// the explorer's header.
/// </summary>
public sealed partial class ProjectFilesViewModel
{
    private PerforceWorkspace? _perforce;
    private bool _perforceStarted;
    private bool _showedPerforceState;

    /// <summary>The check-outs the editors' first edits started, by path, which a save waits for.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task> _checkingOut = new(PathComparer);

    private PerforceFilesService PerforceFiles => _services.Perforce.Files;

    /// <summary>The project's folder is in a Perforce workspace: its entries' menus have a Perforce section.</summary>
    public bool IsPerforceWorkspace => _perforce is not null;

    /// <summary>Settings → Perforce → <b>Show Perforce state in the Files tab</b>.</summary>
    private bool ShowsPerforceState => _services.Settings.Perforce.ShowFileState;

    /// <summary>What <c>p4</c> refused, under the explorer's header in the error color, until it's dismissed or the next command.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPerforceProblem))]
    public partial string? PerforceProblem { get; private set; }

    public bool HasPerforceProblem => PerforceProblem is not null;

    /// <summary>One quiet line when the server doesn't answer: the tree has no marks, and that's all.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPerforceNote))]
    public partial string? PerforceNote { get; private set; }

    public bool HasPerforceNote => PerforceNote is not null;

    /// <summary>What's being asked before a Perforce command, while it's asked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAskingPerforce))]
    public partial PerforceQuestion? PerforceAsking { get; private set; }

    public bool IsAskingPerforce => PerforceAsking is not null;

    /// <summary>The question shows: the view puts the keyboard on its button.</summary>
    public event Action? PerforceAsked;

    /// <summary><b>Diff against the depot</b>: the view shows it in the diff window.</summary>
    public event Action<DiffSource>? DiffRequested;

    // ---- The workspace (DESIGN.md §18, "Finding the workspace") -----------------------------------------------------

    /// <summary>The first time the tab shows: its workspace is looked for in the background, and the marks follow.</summary>
    private void StartPerforce()
    {
        if (_perforceStarted)
        {
            return;
        }
        _perforceStarted = true;
        _showedPerforceState = ShowsPerforceState;
        PerforceFiles.StatusesChanged += OnPerforceStatusesChanged;
        _ = FindPerforceAsync();
    }

    private async Task FindPerforceAsync()
    {
        var detection = await PerforceFiles.DetectAsync(Folder).ConfigureAwait(false);
        _services.Dispatcher.Post(() => OnPerforceFound(detection));
    }

    private void OnPerforceFound(PerforceDetection detection)
    {
        if (_disposed)
        {
            return;
        }
        _perforce = detection.Workspace;
        OnPropertyChanged(nameof(IsPerforceWorkspace));
        if (detection.Outcome == PerforceDetectionOutcome.Unreachable)
        {
            NoteUnreachable();
        }
        ApplyPerforceEverywhere();
        RefreshPerforce(ShownFolders());
    }

    /// <summary>The folders whose files show: the project's, the expanded ones', and the open files'.</summary>
    private IEnumerable<string> ShownFolders() =>
    [
        Folder,
        .. Rows.Where(r => r.IsExpanded && r is not NameRow).Select(r => r.FullPath),
        .. OpenFiles.Where(f => !f.IsOutsideProject).Select(f => Path.GetDirectoryName(f.Path)).OfType<string>(),
    ];

    // ---- The state in the tree (DESIGN.md §18, "The state in the tree") ---------------------------------------------

    /// <summary>
    /// Asks Perforce about the files of these folders, off the UI thread: when a folder is read (expanded, or after the
    /// disk changed), on <b>Refresh</b>, and once the workspace is found. Nothing waits for it.
    /// </summary>
    private void RefreshPerforce(IEnumerable<string> folders)
    {
        if (_perforce is null || !ShowsPerforceState || _disposed)
        {
            return;
        }
        foreach (var folder in folders.Distinct(PathComparer).ToArray())
        {
            // One ask a folder at a time: one asked for meanwhile follows it, once.
            if (_perforceAsking.TryGetValue(folder, out _))
            {
                _perforceAsking[folder] = true;
                continue;
            }
            _perforceAsking[folder] = false;
            _ = RefreshPerforceFolderAsync(folder);
        }
    }

    /// <summary>The folders being asked about, on the UI thread: true when another ask is wanted once the answer is in.</summary>
    private readonly Dictionary<string, bool> _perforceAsking = new(PathComparer);

    private async Task RefreshPerforceFolderAsync(string folder)
    {
        var refresh = await PerforceFiles.RefreshFolderAsync(Folder, folder).ConfigureAwait(false);
        _services.Dispatcher.Post(() =>
        {
            if (_perforceAsking.Remove(folder, out var again) && again)
            {
                RefreshPerforce([folder]);
            }
            switch (refresh)
            {
                case PerforceRefresh.Unreachable:
                    NoteUnreachable();
                    break;
                case PerforceRefresh.NeedsLogin:
                    PerforceNote ??= "Perforce wants you to log in, so the explorer has no Perforce marks. Refresh tries again once you have.";
                    break;
                case PerforceRefresh.Updated:
                    PerforceNote = null;
                    break;
            }
        });
    }

    /// <summary>The server didn't answer: one quiet line, not an error for each folder.</summary>
    private void NoteUnreachable() => PerforceNote ??= "Perforce isn't answering, so the explorer has no Perforce marks. Refresh tries again.";

    /// <summary>Folders' states came in, from this tab or another's command: their rows and open files show them.</summary>
    private void OnPerforceStatusesChanged(IReadOnlyCollection<string> folders)
    {
        if (_disposed || _perforce is null)
        {
            return;
        }
        var changed = new HashSet<string>(folders.Select(Path.TrimEndingDirectorySeparator), PathComparer);
        foreach (var row in Rows)
        {
            if (Path.GetDirectoryName(row.FullPath) is { } parent && changed.Contains(Path.TrimEndingDirectorySeparator(parent)))
            {
                ApplyPerforce(row);
            }
        }
        foreach (var file in OpenFiles)
        {
            if (Path.GetDirectoryName(file.Path) is { } parent && changed.Contains(parent))
            {
                ApplyPerforce(file);
            }
        }
    }

    private void ApplyPerforceEverywhere()
    {
        foreach (var row in Rows)
        {
            ApplyPerforce(row);
        }
        foreach (var file in OpenFiles)
        {
            ApplyPerforce(file);
        }
    }

    /// <summary>A row's Perforce section and mark, from what's cached: nothing is asked here.</summary>
    private void ApplyPerforce(ExplorerRow row)
    {
        if (row is NameRow)
        {
            return;
        }
        var inPerforce = _perforce is { } workspace && !row.IsDeleted && PerforceFilesService.Contains(workspace, Folder, row.FullPath);
        row.InPerforce = inPerforce;
        row.PerforceState = inPerforce && ShowsPerforceState && row.IsFile ? PerforceFiles.Statuses.Find(row.FullPath) : null;
    }

    /// <summary>An open file's Perforce menu, and whether it opens read-only because it isn't checked out.</summary>
    private void ApplyPerforce(ProjectFileViewModel file)
    {
        var inPerforce = _perforce is { } workspace && PerforceFilesService.Contains(workspace, Folder, file.Path);
        file.UpdatePerforce(inPerforce, inPerforce ? PerforceFiles.Statuses.Find(file.Path) : null, _services.Settings.Perforce.CheckOutBeforeChanges);
        if (inPerforce && file.IsWriteProtected && PerforceFiles.Statuses.Find(file.Path) is null)
        {
            // Read-only on disk: whether it's a depot file not checked out yet decides whether it can be edited.
            _ = AskAboutOpenFileAsync(file, file.Path);
        }
    }

    private async Task AskAboutOpenFileAsync(ProjectFileViewModel file, string path)
    {
        var state = await PerforceFiles.StateOfAsync(Folder, path).ConfigureAwait(false);
        _services.Dispatcher.Post(() =>
        {
            if (!_disposed && PathComparer.Equals(file.Path, path) && OpenFiles.Contains(file))
            {
                file.UpdatePerforce(true, state, _services.Settings.Perforce.CheckOutBeforeChanges);
            }
        });
    }

    /// <summary>Settings → Perforce changed: marks come or go, and open files follow <b>Check out files before they're changed</b>.</summary>
    private void OnPerforceSettingsChanged()
    {
        if (!_perforceStarted)
        {
            return;
        }
        var shows = ShowsPerforceState;
        ApplyPerforceEverywhere();
        if (shows && !_showedPerforceState)
        {
            RefreshPerforce(ShownFolders());
        }
        _showedPerforceState = shows;
    }

    /// <summary>The explorer's <b>Refresh</b>: a folder that wasn't a workspace, or whose server didn't answer, is looked at again.</summary>
    private void RefreshPerforceWorkspace()
    {
        if (!_perforceStarted)
        {
            return;
        }
        PerforceNote = null;
        if (_perforce is null)
        {
            PerforceFiles.ForgetWorkspace(Folder);
            _ = FindPerforceAsync();
        }
    }

    // ---- Where commands run, and in which changelist (DESIGN.md §18, "Which changelist") ---------------------------

    /// <summary>
    /// The project's session tab focused most recently: its changelist is the one the Files tab opens files in, and its
    /// ticket keeper, when ticket handling is on, renews an expired login.
    /// </summary>
    internal PerforceContext PerforceContext()
    {
        var tabs = Group.Tabs.OrderByDescending(t => t.FocusOrder).ToArray();
        var renewer = tabs.FirstOrDefault(t => t.Perforce.Workspace is not null);
        return new PerforceContext(Folder, tabs.FirstOrDefault()?.Perforce.WorkingChangelist, renewer is null ? null : renewer.Perforce.RenewLoginAsync);
    }

    [RelayCommand]
    private void DismissPerforceProblem() => PerforceProblem = null;

    // ---- The Perforce menu (DESIGN.md §18, "The Perforce menu") -----------------------------------------------------

    [RelayCommand]
    private Task CheckOutRowAsync(ExplorerRow? row) => row is null or NameRow ? Task.CompletedTask : CheckOutAsync(row.FullPath, row.IsFolder, row.Name);

    [RelayCommand]
    private Task MarkRowForAddAsync(ExplorerRow? row) => row is null or NameRow ? Task.CompletedTask : MarkForAddAsync(row.FullPath, row.IsFolder, row.Name);

    [RelayCommand]
    private Task MarkRowForDeleteAsync(ExplorerRow? row) => row is null or NameRow ? Task.CompletedTask : AskToMarkForDeleteAsync(row.FullPath, row.IsFolder, row.Name);

    [RelayCommand]
    private Task RevertRowAsync(ExplorerRow? row) => row is null or NameRow ? Task.CompletedTask : RevertAsync(row.FullPath, row.IsFolder, row.Name);

    [RelayCommand]
    private Task DiffRowAgainstDepotAsync(ExplorerRow? row) => row is null or NameRow || row.IsFolder ? Task.CompletedTask : DiffAgainstDepotAsync(row.FullPath, row.RelativePath, row.PerforceState);

    [RelayCommand]
    private Task CheckOutFileAsync(ProjectFileViewModel? file) => file is null ? Task.CompletedTask : CheckOutAsync(file.Path, false, file.Name);

    [RelayCommand]
    private Task MarkFileForAddAsync(ProjectFileViewModel? file) => file is null ? Task.CompletedTask : MarkForAddAsync(file.Path, false, file.Name);

    [RelayCommand]
    private Task MarkFileForDeleteAsync(ProjectFileViewModel? file)
    {
        if (file is null)
        {
            return Task.CompletedTask;
        }
        // The question is the explorer's.
        ShowExplorer();
        return AskToMarkForDeleteAsync(file.Path, false, file.Name);
    }

    [RelayCommand]
    private Task RevertFileAsync(ProjectFileViewModel? file)
    {
        if (file is null)
        {
            return Task.CompletedTask;
        }
        ShowExplorer();
        return RevertAsync(file.Path, false, file.Name);
    }

    [RelayCommand]
    private Task DiffFileAgainstDepotAsync(ProjectFileViewModel? file) => file is null ? Task.CompletedTask : DiffAgainstDepotAsync(file.Path, file.RelativePath, file.PerforceState);

    /// <summary><b>Check out</b>: <c>p4 edit</c> of the file, or the folder's (<c>folder/...</c>). Open files of it can be edited then.</summary>
    private async Task CheckOutAsync(string path, bool isFolder, string name)
    {
        if (await RunPerforceAsync($"check out {name}", context => PerforceFiles.CheckOutAsync(context, path, isFolder)))
        {
            AfterCheckOut(path);
        }
    }

    /// <summary><b>Mark for add</b>: <c>p4 add</c> of the file, or <c>p4 reconcile -a</c> of the folder's new files.</summary>
    private Task MarkForAddAsync(string path, bool isFolder, string name) =>
        RunPerforceAsync($"mark {name} for add", context => PerforceFiles.MarkForAddAsync(context, path, isFolder));

    /// <summary>
    /// <b>Mark for delete</b>: asked first, since Perforce deletes the local files, then <c>p4 delete</c>. The files open
    /// from it close, as a delete closes them.
    /// </summary>
    private Task AskToMarkForDeleteAsync(string path, bool isFolder, string name)
    {
        CancelNaming();
        Deleting = null;
        var what = isFolder ? $"the files in {name}" : name;
        Ask(new PerforceQuestion($"Mark {what} for delete? Perforce deletes {(isFolder ? "them" : "it")} from the disk; Revert brings {(isFolder ? "them" : "it")} back.{UnsavedNote(path)}",
            "Mark for delete", async () =>
            {
                if (await RunPerforceAsync($"mark {name} for delete", context => PerforceFiles.MarkForDeleteAsync(context, path, isFolder)))
                {
                    _shell.CloseDeleted(path);
                    _foldersToRead.Add(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path))!);
                    await ReadQueuedFoldersAsync();
                }
            }));
        return Task.CompletedTask;
    }

    /// <summary>
    /// <b>Revert</b>: asked first when it would lose changes (<c>p4 diff -sa</c> lists it, or Perforce couldn't say),
    /// straight away when it wouldn't. Open files of it read what's on disk again.
    /// </summary>
    private async Task RevertAsync(string path, bool isFolder, string name)
    {
        CancelNaming();
        PerforceProblem = null;
        var hasChanges = await PerforceFiles.HasChangesAsync(PerforceContext(), path, isFolder);
        if (hasChanges == false)
        {
            await RevertNowAsync(path, isFolder, name);
            return;
        }
        Deleting = null;
        var what = isFolder ? $"the files in {name}" : name;
        Ask(new PerforceQuestion($"Revert {what}? {(isFolder ? "Their" : "Its")} changes are lost.{UnsavedNote(path)}", "Revert", () => RevertNowAsync(path, isFolder, name)));
    }

    private async Task RevertNowAsync(string path, bool isFolder, string name)
    {
        if (await RunPerforceAsync($"revert {name}", context => PerforceFiles.RevertAsync(context, path, isFolder)))
        {
            // Unsaved edits of it would be written over the depot's copy just restored: they go, as the question said.
            foreach (var file in _shell.OpenFilesAtOrUnder(path).Where(f => f.IsModified).ToArray())
            {
                file.DiscardEdits();
            }
            _foldersToRead.Add(isFolder ? path : Path.GetDirectoryName(path)!);
            await ReadQueuedFoldersAsync();
            await CheckOpenFilesOnDiskAsync();
        }
    }

    /// <summary>
    /// <b>Diff against the depot</b>: the revision synced here (<c>p4 print -q file#have</c>) against the file, in the diff
    /// tool when Settings → Files and diffs has one, else in the diff window.
    /// </summary>
    private async Task DiffAgainstDepotAsync(string path, string relativePath, PerforceFileState? state)
    {
        PerforceProblem = null;
        var temp = Path.Combine(_services.Paths.DiffTempDirectory, Id);
        var (text, outcome) = await PerforceFiles.HaveRevisionAsync(PerforceContext(), path, temp);
        if (text is null)
        {
            PerforceProblem = $"Couldn't get {Path.GetFileName(path)} from the depot: {outcome.Problem}";
            return;
        }
        var tool = ChangedFilesViewModel.DiffToolFrom(_services.Settings.DiffTool);
        Func<Task>? inDiffTool = tool.Kind == DiffToolKind.BuiltIn ? null : () => OpenInDiffToolAsync(tool, text, path, temp);
        if (inDiffTool is not null)
        {
            await inDiffTool();
            return;
        }
        var label = state?.HaveRevision is { } have ? $"compared with #{have}, the revision you have from the depot" : "compared with the revision you have from the depot";
        DiffRequested?.Invoke(new DiffSource(
            path,
            relativePath,
            text,
            label,
            inDiffTool,
            async () => (await OpenFileAsync(path, preview: false, focus: true)) is not null,
            () => RevealAsync(path),
            () => CopyPathAsync(path),
            AllowRevert: false,
            OpenInSystem: () => OpenInSystemAsync(path),
            WordWrap: DiffWordWrap.From(_services)));
    }

    private async Task OpenInDiffToolAsync(DiffToolChoice tool, string depotText, string path, string temp)
    {
        try
        {
            var launcher = new DiffToolLauncher(_services.Launcher, _services.Time, environment: _services.UserEnvironment);
            await launcher.LaunchAsync(tool, depotText, path, temp);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            PerforceProblem = $"Couldn't open the diff tool: {ex.Message}";
        }
    }

    /// <summary>Runs a command the user chose; what Perforce refused shows under the header. True when it worked.</summary>
    private async Task<bool> RunPerforceAsync(string what, Func<PerforceContext, Task<PerforceOutcome>> run)
    {
        PerforceProblem = null;
        var outcome = await run(PerforceContext());
        if (!outcome.Succeeded)
        {
            PerforceProblem = $"Couldn't {what}: {outcome.Problem}";
            return false;
        }
        return true;
    }

    private void Ask(PerforceQuestion question)
    {
        PerforceAsking = question;
        PerforceAsked?.Invoke();
    }

    /// <summary>The question's button: the command runs, and the question goes.</summary>
    [RelayCommand]
    private async Task ConfirmPerforceAsync()
    {
        if (PerforceAsking is not { IsRunning: false } question)
        {
            return;
        }
        question.IsRunning = true;
        try
        {
            await question.Confirm();
        }
        finally
        {
            if (ReferenceEquals(PerforceAsking, question))
            {
                PerforceAsking = null;
                TreeFocusRequested?.Invoke();
            }
        }
    }

    /// <summary>The question's <b>Cancel</b>, or <c>Esc</c>: nothing is done.</summary>
    [RelayCommand]
    private void CancelPerforce()
    {
        if (PerforceAsking is { IsRunning: false })
        {
            PerforceAsking = null;
            TreeFocusRequested?.Invoke();
        }
    }

    /// <summary>Checked out: the open files of it that were read-only on disk can be edited now.</summary>
    private void AfterCheckOut(string path)
    {
        foreach (var file in OpenFilesAtOrUnder(path))
        {
            file.OnCheckedOut();
        }
    }

    // ---- The automatic actions (DESIGN.md §18, "Opening files in Perforce") ---------------------------------------

    /// <summary>
    /// An open file's text first changed: a depot file not checked out yet is checked out in the background, so it's
    /// writable before it's saved. A save that comes first waits for it (<see cref="WaitForCheckOutAsync"/>). Nothing
    /// waits on the UI thread, and the edit is never held back or lost: a check-out that fails says why.
    /// </summary>
    private void CheckOutForEdit(ProjectFileViewModel file)
    {
        if (!_services.Settings.Perforce.CheckOutBeforeChanges || file.IsOutsideProject && !file.InPerforce)
        {
            return;
        }
        // Known not to need it: a file Perforce doesn't have, or has open already.
        if (file.InPerforce && file.PerforceState is { } state && state.Status != PerforceFileStatus.InDepot)
        {
            return;
        }
        var path = file.Path;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Recorded before it starts, so a save right after finds it.
        if (!_checkingOut.TryAdd(path, done.Task))
        {
            return;
        }
        var context = PerforceContext();
        _ = CheckOutInBackgroundAsync();

        async Task CheckOutInBackgroundAsync()
        {
            try
            {
                var outcome = await PerforceFiles.CheckOutBeforeWritingAsync(context, [path]).ConfigureAwait(false);
                _services.Dispatcher.Post(() =>
                {
                    if (!outcome.Succeeded)
                    {
                        PerforceProblem = $"Couldn't check out {Path.GetFileName(path)}: {outcome.Problem}";
                    }
                    else if (OpenFiles.Contains(file))
                    {
                        file.OnCheckedOut();
                    }
                });
            }
            finally
            {
                _checkingOut.TryRemove(new KeyValuePair<string, Task>(path, done.Task));
                done.TrySetResult();
            }
        }
    }

    /// <summary>A save waits for the check-out the file's edits started, as long as <see cref="PerforceFilesService.SaveWait"/>.</summary>
    internal async Task WaitForCheckOutAsync(string path)
    {
        if (!_checkingOut.TryGetValue(path, out var checkingOut))
        {
            return;
        }
        try
        {
            await checkingOut.WaitAsync(PerforceFilesService.SaveWait, _services.Time);
        }
        catch (TimeoutException)
        {
            // Written anyway: a file Perforce still has read-only says so as the save fails, and the edits stay.
        }
    }

    /// <summary>A file made with <b>New file</b> is marked for add, as Settings → Perforce → <b>Add new files</b> says.</summary>
    private void AddNewFileToPerforce(string path) =>
        RunAutomatically($"mark {Path.GetFileName(path)} for add", context => PerforceFiles.AddNewFilesAsync(context, [path]));

    /// <summary>A file or folder deleted from the explorer is marked for delete, as <b>Mark deleted files for delete</b> says.</summary>
    private void MarkDeletedInPerforce(string path, bool isFolder) =>
        RunAutomatically($"mark {Path.GetFileName(path)} for delete", context => PerforceFiles.OnDeletedAsync(context, path, isFolder));

    /// <summary>A file or folder renamed in the explorer is moved in Perforce, as <b>Rename with p4 move</b> says.</summary>
    private void MoveInPerforce(string oldPath, string newPath, bool isFolder) =>
        RunAutomatically($"move {Path.GetFileName(oldPath)} to {Path.GetFileName(newPath)} in Perforce", context => PerforceFiles.OnRenamedAsync(context, oldPath, newPath, isFolder));

    /// <summary>
    /// Settings → Perforce → <b>Check out files before they're changed</b>, for Claudette's own writes, such as replacing
    /// text in files: the depot files among <paramref name="paths"/> are checked out first, in the changelist of the
    /// project's tab focused last. What Perforce refuses shows under the explorer's header; the files are still written.
    /// </summary>
    internal async Task CheckOutBeforeWritingAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        var outcome = await PerforceFiles.CheckOutBeforeWritingAsync(PerforceContext(), paths, cancellationToken);
        if (!outcome.Succeeded)
        {
            PerforceProblem = $"Couldn't check out every file first: {outcome.Problem}";
        }
        foreach (var path in paths)
        {
            AfterCheckOut(path);
        }
    }

    /// <summary>An automatic action, in the background: what Perforce refused shows under the header.</summary>
    private void RunAutomatically(string what, Func<PerforceContext, Task<PerforceOutcome>> run)
    {
        var context = PerforceContext();
        _ = RunInBackgroundAsync();

        async Task RunInBackgroundAsync()
        {
            var outcome = await run(context).ConfigureAwait(false);
            if (!outcome.Succeeded)
            {
                _services.Dispatcher.Post(() => PerforceProblem = $"Couldn't {what}: {outcome.Problem}");
            }
        }
    }

    private void StopPerforce()
    {
        if (_perforceStarted)
        {
            PerforceFiles.StatusesChanged -= OnPerforceStatusesChanged;
        }
    }
}
