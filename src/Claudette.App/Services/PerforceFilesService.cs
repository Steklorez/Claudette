using Claudette.Core.Diffs;
using Claudette.Core.Perforce;
using Claudette.Core.Settings;

namespace Claudette.App.Services;

/// <summary>
/// Where a Perforce command for files runs (DESIGN.md §18, "Perforce in the Files tab"): in which folder, so Perforce
/// finds its settings there; in which changelist (null: the default one); and how an expired login is renewed before
/// the command is tried once more (null: it isn't).
/// </summary>
public sealed record PerforceContext(string Folder, long? Changelist = null, Func<CancellationToken, Task<bool>>? RenewLogin = null);

/// <summary>What a Perforce command for files came to: done, or Perforce's words for why not.</summary>
/// <param name="Unreachable">The server didn't answer, or <c>p4</c> couldn't be run.</param>
public sealed record PerforceOutcome(bool Succeeded, string? Problem = null, bool Unreachable = false)
{
    public static readonly PerforceOutcome Done = new(true);
}

/// <summary>What asking about a folder's files came to.</summary>
public enum PerforceRefresh
{
    Updated,

    /// <summary>A later ask began meanwhile: this one's answer was dropped.</summary>
    Overtaken,

    /// <summary>The server didn't answer: no marks, and no error.</summary>
    Unreachable,

    /// <summary>Perforce wants a login first.</summary>
    NeedsLogin,

    Failed,
}

/// <summary>
/// Perforce for files, shared by the Files tabs and the session tabs (DESIGN.md §18, "Perforce in the Files tab" and
/// "Opening files in Perforce"): finding a folder's workspace in the background, the files' states and their cache, the
/// commands of the Files tab's menus, and the automatic actions Settings → Perforce turns on. Every <c>p4</c> runs off the
/// UI thread, bounded by a timeout on the injected clock, and none of it throws: a command that didn't work says why.
/// </summary>
public sealed class PerforceFilesService
{
    /// <summary>
    /// Where a project's files are opened in Perforce, by a Files tab or Replace in files: <paramref name="folder"/>, in the
    /// changelist of the project's session tab focused most recently (DESIGN.md §18, "Which changelist"), whose ticket
    /// keeper, when ticket handling is on, renews an expired login. On the UI thread, where the tabs are.
    /// </summary>
    internal static PerforceContext ContextFor(string folder, IEnumerable<ViewModels.TabViewModel> projectTabs)
    {
        var tabs = projectTabs.OrderByDescending(t => t.FocusOrder).ToArray();
        var renewer = tabs.FirstOrDefault(t => t.Perforce.Workspace is not null);
        return new PerforceContext(folder, tabs.FirstOrDefault()?.Perforce.WorkingChangelist, renewer is null ? null : renewer.Perforce.RenewLoginAsync);
    }

    /// <summary>A command run for a file or a few, by the user or by Claudette.</summary>
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);

    /// <summary>A folder's states, which only marks wait for.</summary>
    public static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(15);

    /// <summary>A command on everything in a folder (<c>folder/...</c>), which the user chose, and which can take a while.</summary>
    public static readonly TimeSpan FolderCommandTimeout = TimeSpan.FromMinutes(2);

    /// <summary>How long Claude Code waits for the check-out hook before its file tools.</summary>
    public static readonly TimeSpan HookTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The hook answers by itself this long before Claude Code would give up on it, which would stop the tool.</summary>
    public static readonly TimeSpan HookMargin = TimeSpan.FromSeconds(15);

    /// <summary>How long a save waits for the check-out its edits started, before writing anyway.</summary>
    public static readonly TimeSpan SaveWait = TimeSpan.FromSeconds(30);

    /// <summary>How many characters of paths one command takes, so a long list goes in several, under every system's limit.</summary>
    private const int MaxArgumentsLength = 8000;

    private readonly AppServices _services;
    private readonly PerforceService _perforce;

    internal PerforceFilesService(AppServices services, PerforceService perforce)
    {
        _services = services;
        _perforce = perforce;
        Workspaces = new PerforceWorkspaceFinder(perforce.Client, services.Time);
    }

    /// <summary>Each folder's workspace, found once in the background.</summary>
    public PerforceWorkspaceFinder Workspaces { get; }

    /// <summary>The states of the files in the folders asked about, which the explorer draws its marks from.</summary>
    public PerforceStatusCache Statuses { get; } = new(ChangedFiles.PlatformPathComparer);

    private PerforceSettings Settings => _services.Settings.Perforce;

    private PerforceClient Client => _perforce.Client;

    /// <summary>The states of the files in these folders changed: raised on the UI thread.</summary>
    public event Action<IReadOnlyCollection<string>>? StatusesChanged;

    // ---- Workspaces (DESIGN.md §18, "Finding the workspace") ------------------------------------------------------

    /// <summary>The workspace <paramref name="folder"/> is in, found now or before; null when it isn't in one.</summary>
    public Task<PerforceWorkspace?> WorkspaceForAsync(string folder, CancellationToken cancellationToken = default) =>
        Workspaces.FindAsync(_perforce.TargetFor(folder), cancellationToken);

    /// <summary>What finding <paramref name="folder"/>'s workspace came to, found now or before.</summary>
    public Task<PerforceDetection> DetectAsync(string folder, CancellationToken cancellationToken = default) =>
        Workspaces.DetectAsync(_perforce.TargetFor(folder), cancellationToken);

    /// <summary>The workspace <paramref name="folder"/> is in, when that's been found; null while it's being looked for.</summary>
    public PerforceWorkspace? KnownWorkspace(string folder) => Workspaces.Known(_perforce.TargetFor(folder));

    /// <summary>The explorer's <b>Refresh</b>: the folder's workspace is looked for again next time.</summary>
    public void ForgetWorkspace(string folder) => Workspaces.Forget(_perforce.TargetFor(folder));

    /// <summary>Ticket handling found the folder's workspace as the tab started.</summary>
    internal void RememberWorkspace(PerforceTarget target, PerforceWorkspace workspace) => Workspaces.Remember(target, workspace);

    /// <summary>
    /// Whether <paramref name="path"/> is in the workspace: under its root, or for a workspace without one (a root of
    /// <c>null</c>, its view decides), under the folder it was found for.
    /// </summary>
    public static bool Contains(PerforceWorkspace workspace, string folder, string path)
    {
        var root = workspace.ClientRoot ?? folder;
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var top = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var prefix = Path.EndsInDirectorySeparator(top) ? top : top + Path.DirectorySeparatorChar;
            return full.Equals(top, comparison) || full.StartsWith(prefix, comparison)
                // The root may be written another way than the folder (a link, macOS's /private/var): the folder's own
                // files are the workspace's whenever the folder is.
                || full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar, comparison)
                || full.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    // ---- The files' states (DESIGN.md §18, "The state in the tree") ------------------------------------------------

    /// <summary>
    /// Asks Perforce about the files directly in <paramref name="folder"/>, in one <c>p4 -ztag fstat folder/*</c>, and keeps
    /// the answer. Off the UI thread; <see cref="StatusesChanged"/> says when it's kept. An answer a later ask overtook
    /// is dropped.
    /// </summary>
    public async Task<PerforceRefresh> RefreshFolderAsync(string projectFolder, string folder, CancellationToken cancellationToken = default)
    {
        var ask = Statuses.Begin(folder);
        var result = await Task.Run(() => Client.RunCommandAsync(_perforce.TargetFor(projectFolder), PerforceFileCommands.Fstat([PerforceFileCommands.FilesInFolderSpec(folder)]),
            StatusTimeout, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (!result.Ran || result.IsConnectionFailure)
        {
            Statuses.Abandon(folder, ask);
            return PerforceRefresh.Unreachable;
        }
        if (result.NeedsLogin)
        {
            Statuses.Abandon(folder, ask);
            return PerforceRefresh.NeedsLogin;
        }
        var states = PerforceFileState.ParseFstat(result.Output);
        // "no such file(s)": none of the folder's files are Perforce's.
        if (states.Count == 0 && PerforceFileCommands.Problem(result, PerforceQuietness.Automatic) is not null)
        {
            Statuses.Abandon(folder, ask);
            return PerforceRefresh.Failed;
        }
        if (!Statuses.Complete(folder, ask, states))
        {
            return PerforceRefresh.Overtaken;
        }
        _services.Dispatcher.Post(() => StatusesChanged?.Invoke([folder]));
        return PerforceRefresh.Updated;
    }

    /// <summary>
    /// <paramref name="path"/>'s state: from the cache, or asked about on its own (off the UI thread). Null when Perforce
    /// couldn't say.
    /// </summary>
    public async Task<PerforceFileState?> StateOfAsync(string projectFolder, string path, CancellationToken cancellationToken = default)
    {
        if (Statuses.Find(path) is { } known)
        {
            return known;
        }
        var result = await Task.Run(() => Client.RunCommandAsync(_perforce.TargetFor(projectFolder), PerforceFileCommands.Fstat([PerforceFileCommands.FileSpec(path)]),
            StatusTimeout, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (!result.Ran || result.IsConnectionFailure || result.NeedsLogin)
        {
            return null;
        }
        if (PerforceFileState.ParseFstat(result.Output).FirstOrDefault() is { } state)
        {
            return state with { Path = path };
        }
        return PerforceFileCommands.Problem(result, PerforceQuietness.Automatic) is null ? PerforceFileState.NotInDepotAt(path) : null;
    }

    /// <summary>The folders whose states a command on these paths changed, and which are kept: asked about again.</summary>
    private async Task RefreshAfterAsync(string projectFolder, IEnumerable<(string Path, bool IsFolder)> paths)
    {
        var folders = new HashSet<string>(ChangedFiles.PlatformPathComparer);
        foreach (var (path, isFolder) in paths)
        {
            if (isFolder)
            {
                folders.UnionWith(Statuses.FoldersAtOrUnder(path));
            }
            if (Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) is { } parent && Statuses.Knows(parent))
            {
                folders.Add(parent);
            }
        }
        await Task.WhenAll(folders.Select(f => RefreshFolderAsync(projectFolder, f))).ConfigureAwait(false);
    }

    // ---- Commands (DESIGN.md §18, "The Perforce menu") -------------------------------------------------------------

    /// <summary><c>p4 edit</c>: check out a file, or a folder's files.</summary>
    public Task<PerforceOutcome> CheckOutAsync(PerforceContext context, string path, bool isFolder, CancellationToken cancellationToken = default) =>
        RunAndRefreshAsync(context, [(path, isFolder)], _ => PerforceFileCommands.Edit([PerforceFileCommands.Spec(path, isFolder)], context.Changelist),
            PerforceQuietness.Asked, isFolder ? FolderCommandTimeout : CommandTimeout, cancellationToken);

    /// <summary><c>p4 add</c> for a file; for a folder, <c>p4 reconcile -a folder/...</c>, which opens its new files.</summary>
    public Task<PerforceOutcome> MarkForAddAsync(PerforceContext context, string path, bool isFolder, CancellationToken cancellationToken = default) =>
        RunAndRefreshAsync(context, [(path, isFolder)], _ => isFolder ? PerforceFileCommands.ReconcileAdd(path, context.Changelist) : PerforceFileCommands.Add([path], context.Changelist),
            PerforceQuietness.Asked, isFolder ? FolderCommandTimeout : CommandTimeout, cancellationToken);

    /// <summary><c>p4 delete</c>: mark for delete, which deletes the local files as Perforce does.</summary>
    public Task<PerforceOutcome> MarkForDeleteAsync(PerforceContext context, string path, bool isFolder, CancellationToken cancellationToken = default) =>
        RunAndRefreshAsync(context, [(path, isFolder)], _ => PerforceFileCommands.Delete([PerforceFileCommands.Spec(path, isFolder)], context.Changelist, keepLocal: false),
            PerforceQuietness.Asked, isFolder ? FolderCommandTimeout : CommandTimeout, cancellationToken);

    /// <summary><c>p4 revert</c>: the files go back to the depot's, and their changes are lost.</summary>
    public Task<PerforceOutcome> RevertAsync(PerforceContext context, string path, bool isFolder, CancellationToken cancellationToken = default) =>
        RunAndRefreshAsync(context, [(path, isFolder)], _ => PerforceFileCommands.Revert([PerforceFileCommands.Spec(path, isFolder)], keepLocal: false),
            PerforceQuietness.Asked, isFolder ? FolderCommandTimeout : CommandTimeout, cancellationToken);

    /// <summary>
    /// <c>p4 diff -sa</c>: whether reverting would lose changes. True when it would, false when not, null when Perforce
    /// couldn't say (then <b>Revert</b> asks, to be safe).
    /// </summary>
    public async Task<bool?> HasChangesAsync(PerforceContext context, string path, bool isFolder, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(context, PerforceFileCommands.DiffChanged([PerforceFileCommands.Spec(path, isFolder)]), isFolder ? FolderCommandTimeout : CommandTimeout, cancellationToken).ConfigureAwait(false);
        if (!result.Ran || result.IsConnectionFailure || result.NeedsLogin)
        {
            return null;
        }
        return result.Output.Split('\n').Any(l => l.Trim().Length > 0);
    }

    /// <summary>
    /// <c>p4 print -q -o file file#have</c>: the revision synced here, as text, for <b>Diff against the depot</b>. It's
    /// written to a file in <paramref name="tempFolder"/> as it is, so its line endings and encoding are the depot's, then
    /// read and deleted.
    /// </summary>
    public async Task<(string? Text, PerforceOutcome Outcome)> HaveRevisionAsync(PerforceContext context, string path, string tempFolder, CancellationToken cancellationToken = default)
    {
        var output = Path.Combine(tempFolder, $"{Guid.NewGuid():N}-{Path.GetFileName(path)}");
        try
        {
            Directory.CreateDirectory(tempFolder);
            var result = await RunAsync(context, PerforceFileCommands.PrintHave(path, output), CommandTimeout, cancellationToken).ConfigureAwait(false);
            if (Outcome(result, PerforceQuietness.Asked) is { Succeeded: false } failed)
            {
                return (null, failed);
            }
            var text = await Task.Run(() => Core.ProjectFiles.TextFile.ReadTextOrNull(output), cancellationToken).ConfigureAwait(false);
            return text is null ? (null, new PerforceOutcome(false, "Perforce didn't send the depot's copy.")) : (text, PerforceOutcome.Done);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, new PerforceOutcome(false, ex.Message));
        }
        finally
        {
            DeleteQuietly(output);
        }
    }

    // ---- The automatic actions (DESIGN.md §18, "Opening files in Perforce") ---------------------------------------

    /// <summary>
    /// Makes files ready to be written, as Settings → Perforce → <b>Check out files before they're changed</b> says: the
    /// depot files among them that aren't checked out yet are, in one <c>p4 edit</c>, in the context's changelist. Files
    /// outside a workspace, ones not on disk yet, and ones Perforce doesn't have or already has open are left alone.
    /// Call it before writing files Perforce may have left read-only, as replacing text in files does. Never throws; a
    /// file it couldn't check out is in the outcome's problem, and the caller still writes what it can.
    /// </summary>
    public async Task<PerforceOutcome> CheckOutBeforeWritingAsync(PerforceContext context, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (!Settings.CheckOutBeforeChanges || paths.Count == 0)
        {
            return PerforceOutcome.Done;
        }
        var workspace = await WorkspaceForAsync(context.Folder, cancellationToken).ConfigureAwait(false);
        if (workspace is null)
        {
            return PerforceOutcome.Done;
        }
        var files = await Task.Run(() => paths
            .Where(p => Contains(workspace, context.Folder, p) && File.Exists(p))
            .Where(p => Statuses.Find(p) is not { } state || state.Status == PerforceFileStatus.InDepot)
            .Distinct(ChangedFiles.PlatformPathComparer)
            .ToArray(), cancellationToken).ConfigureAwait(false);
        if (files.Length == 0)
        {
            return PerforceOutcome.Done;
        }
        return await RunAndRefreshAsync(context, files.Select(f => (f, false)), batch => PerforceFileCommands.Edit(batch.Select(PerforceFileCommands.FileSpec), context.Changelist),
            PerforceQuietness.Automatic, CommandTimeout, cancellationToken, files).ConfigureAwait(false);
    }

    /// <summary>
    /// New files, made with the Files tab's <b>New file</b> or by Claude's Write, are marked for add, as Settings →
    /// Perforce → <b>Add new files</b> says. Perforce's own ignore rules (P4IGNORE) decide which are.
    /// </summary>
    public async Task<PerforceOutcome> AddNewFilesAsync(PerforceContext context, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (!Settings.AddNewFiles || await WorkspaceForAsync(context.Folder, cancellationToken).ConfigureAwait(false) is not { } workspace)
        {
            return PerforceOutcome.Done;
        }
        var files = paths.Where(p => Contains(workspace, context.Folder, p)).ToArray();
        return files.Length == 0
            ? PerforceOutcome.Done
            : await RunAndRefreshAsync(context, files.Select(f => (f, false)), batch => PerforceFileCommands.Add(batch, context.Changelist),
                PerforceQuietness.Automatic, CommandTimeout, cancellationToken, files).ConfigureAwait(false);
    }

    /// <summary>
    /// A file or folder deleted from the Files tab (to the OS's trash) is marked for delete, as Settings → Perforce →
    /// <b>Mark deleted files for delete</b> says: <c>p4 revert -k</c> first, so a file opened for add is no longer, and one
    /// opened for edit can be deleted, then <c>p4 delete -k</c>, which leaves the local files (already gone) alone.
    /// </summary>
    public async Task<PerforceOutcome> OnDeletedAsync(PerforceContext context, string path, bool isFolder, CancellationToken cancellationToken = default)
    {
        if (!Settings.DeleteRemovedFiles || await WorkspaceForAsync(context.Folder, cancellationToken).ConfigureAwait(false) is not { } workspace
            || !Contains(workspace, context.Folder, path))
        {
            return PerforceOutcome.Done;
        }
        var spec = PerforceFileCommands.Spec(path, isFolder);
        var timeout = isFolder ? FolderCommandTimeout : CommandTimeout;
        var known = isFolder ? null : Statuses.Find(path);
        if (known is null || known.IsOpen)
        {
            var reverted = Outcome(await RunAsync(context, PerforceFileCommands.Revert([spec], keepLocal: true), timeout, cancellationToken).ConfigureAwait(false), PerforceQuietness.Automatic);
            if (!reverted.Succeeded)
            {
                await RefreshAfterAsync(context.Folder, [(path, isFolder)]).ConfigureAwait(false);
                return reverted;
            }
        }
        var outcome = known is { Status: PerforceFileStatus.NotInDepot or PerforceFileStatus.MarkedForAdd }
            ? PerforceOutcome.Done
            : Outcome(await RunAsync(context, PerforceFileCommands.Delete([spec], context.Changelist, keepLocal: true), timeout, cancellationToken).ConfigureAwait(false),
                PerforceQuietness.Automatic);
        await RefreshAfterAsync(context.Folder, [(path, isFolder)]).ConfigureAwait(false);
        return outcome;
    }

    /// <summary>
    /// A file or folder renamed in the Files tab (on disk, already) is moved in Perforce, as Settings → Perforce → <b>Rename
    /// with p4 move</b> says: <c>p4 edit</c> for what isn't open yet, then <c>p4 move -k</c>, which moves it on the server
    /// and leaves the local files alone. A file Perforce doesn't have is left as it is.
    /// </summary>
    public async Task<PerforceOutcome> OnRenamedAsync(PerforceContext context, string oldPath, string newPath, bool isFolder, CancellationToken cancellationToken = default)
    {
        if (!Settings.MoveRenamedFiles || await WorkspaceForAsync(context.Folder, cancellationToken).ConfigureAwait(false) is not { } workspace
            || !Contains(workspace, context.Folder, oldPath) || !Contains(workspace, context.Folder, newPath))
        {
            return PerforceOutcome.Done;
        }
        var timeout = isFolder ? FolderCommandTimeout : CommandTimeout;
        var from = PerforceFileCommands.Spec(oldPath, isFolder);
        var to = PerforceFileCommands.Spec(newPath, isFolder);
        if (!isFolder)
        {
            var state = await StateOfAsync(context.Folder, oldPath, cancellationToken).ConfigureAwait(false);
            if (state is null or { Status: PerforceFileStatus.NotInDepot or PerforceFileStatus.MarkedForDelete or PerforceFileStatus.MovedAway })
            {
                return state is null ? new PerforceOutcome(false, "Perforce couldn't say whether it has the file, so it wasn't moved there.") : PerforceOutcome.Done;
            }
            if (state.Status == PerforceFileStatus.InDepot)
            {
                var opened = Outcome(await RunAsync(context, PerforceFileCommands.Edit([from], context.Changelist), timeout, cancellationToken).ConfigureAwait(false), PerforceQuietness.Asked);
                if (!opened.Succeeded)
                {
                    return opened;
                }
            }
        }
        else
        {
            var opened = Outcome(await RunAsync(context, PerforceFileCommands.Edit([from], context.Changelist), timeout, cancellationToken).ConfigureAwait(false), PerforceQuietness.Automatic);
            if (!opened.Succeeded)
            {
                return opened;
            }
        }
        var moved = Outcome(await RunAsync(context, PerforceFileCommands.Move(from, to, context.Changelist), timeout, cancellationToken).ConfigureAwait(false),
            isFolder ? PerforceQuietness.Automatic : PerforceQuietness.Asked);
        await RefreshAfterAsync(context.Folder, [(oldPath, isFolder), (newPath, isFolder)]).ConfigureAwait(false);
        return moved;
    }

    // ---- Running ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs a command, in batches when it names many files, then asks again about the folders it changed. An expired
    /// login is renewed and the batch tried once more.
    /// </summary>
    private async Task<PerforceOutcome> RunAndRefreshAsync(PerforceContext context, IEnumerable<(string Path, bool IsFolder)> changed, Func<IReadOnlyList<string>, IReadOnlyList<string>> arguments,
        PerforceQuietness quietness, TimeSpan timeout, CancellationToken cancellationToken, IReadOnlyList<string>? files = null)
    {
        var problems = new List<string>();
        var unreachable = false;
        foreach (var batch in Batches(files ?? [""]))
        {
            var outcome = Outcome(await RunAsync(context, arguments(batch), timeout, cancellationToken).ConfigureAwait(false), quietness);
            if (!outcome.Succeeded)
            {
                problems.Add(outcome.Problem ?? "");
                unreachable |= outcome.Unreachable;
                if (outcome.Unreachable)
                {
                    break;
                }
            }
        }
        await RefreshAfterAsync(context.Folder, changed).ConfigureAwait(false);
        return problems.Count == 0 ? PerforceOutcome.Done : new PerforceOutcome(false, string.Join(" ", problems.Where(p => p.Length > 0)), unreachable);
    }

    private async Task<PerforceCommandResult> RunAsync(PerforceContext context, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var target = _perforce.TargetFor(context.Folder);
        var result = await Task.Run(() => Client.RunCommandAsync(target, arguments, timeout, cancellationToken), cancellationToken).ConfigureAwait(false);
        // Ticket handling, when it's on, renews the login, and the command is tried once more (DESIGN.md §18).
        if (result is { Ran: true, NeedsLogin: true } && context.RenewLogin is { } renew && await renew(cancellationToken).ConfigureAwait(false))
        {
            result = await Task.Run(() => Client.RunCommandAsync(target, arguments, timeout, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    private static PerforceOutcome Outcome(PerforceCommandResult result, PerforceQuietness quietness)
    {
        if (!result.Ran || result.IsConnectionFailure)
        {
            return new PerforceOutcome(false, PerforceFileCommands.Problem(result) ?? "Perforce's server didn't answer.", Unreachable: true);
        }
        return PerforceFileCommands.Problem(result, quietness) is { } problem ? new PerforceOutcome(false, problem) : PerforceOutcome.Done;
    }

    /// <summary>The files in groups whose paths fit on one command line; one empty group for a command that names none.</summary>
    private static IEnumerable<IReadOnlyList<string>> Batches(IReadOnlyList<string> files)
    {
        var batch = new List<string>();
        var length = 0;
        foreach (var file in files)
        {
            if (batch.Count > 0 && length + file.Length > MaxArgumentsLength)
            {
                yield return batch;
                batch = [];
                length = 0;
            }
            batch.Add(file);
            length += file.Length + 3;
        }
        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                // print -o keeps the depot's file type, which can be read-only, and Windows won't delete that.
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
