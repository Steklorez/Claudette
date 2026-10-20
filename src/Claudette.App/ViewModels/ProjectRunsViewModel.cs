using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Claudette.App.Conversation;
using Claudette.App.Services;
using Claudette.Core.Processes;
using Claudette.Core.ProjectTools;
using Claudette.Core.Protocol;
using Claudette.Platform.Processes;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Claudette.App.ViewModels;

/// <summary>What the runs of a tab's project jobs need from its project tools, and through them from the tab.</summary>
internal interface IProjectRunsHost
{
    string Id { get; }

    /// <summary>The tab's name, for the "project action finished" notification.</summary>
    string DisplayName { get; }

    /// <summary>The tab's working folder, which a relative path in a log is read against after the run's own.</summary>
    string Folder { get; }

    /// <summary>The folder of the tab's group, which its runs' logs are in too.</summary>
    string GroupFolder { get; }

    void AddNote(string text, NoteKind kind = NoteKind.Info);

    /// <summary>A run began: its log joins the tabs the panes can show.</summary>
    void RunAdded(ProjectRunViewModel run);

    /// <summary>A run was closed: its log leaves the panes, and while it had the focus, the tab takes its place.</summary>
    void RunRemoved(ProjectRunViewModel run);

    /// <summary>Shows a run's log in the main area: in the focused pane, or focused where it's on screen already.</summary>
    void ShowRun(ProjectRunViewModel run);

    /// <summary>A run's log took the focus.</summary>
    void RunSelected(ProjectRunViewModel run);

    /// <summary><b>Close pane</b> for a run's log: it leaves the screen, and stays in the sidebar.</summary>
    void CloseRunPane(ProjectRunViewModel run);

    /// <summary>Opens a file or folder a log names, as the tab opens one a reply names.</summary>
    Task<OpenedPath> OpenPathAsync(string path, int? line, int? column);

    /// <summary>A job started or stopped running: the project's row names the running one.</summary>
    void RunningRunChanged();

    /// <summary>A job began: the project's row, menu and actions change.</summary>
    void JobBegan();

    /// <summary>
    /// A job ended, and its run is still listed: the project's row, menu and actions change, <paramref name="next"/>
    /// runs (Build and launch: the editor only starts after a build that worked), and the project is looked for again.
    /// </summary>
    void JobEnded(ProjectAction? next);
}

/// <summary>
/// The runs of a tab's project jobs (DESIGN.md §18, "Project tools"): each job this tab has run, with its own log, which
/// shows in the main area, the one running, the running entry's ticking time, and the running job's process tree, for
/// the process monitor and Stop.
/// </summary>
public sealed partial class ProjectRunsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly IProjectRunsHost _host;
    /// <summary>The running job's process tree, for the process monitor and Stop.</summary>
    private ProcessTree? _jobTree;
    /// <summary>A running entry's time ticks every second.</summary>
    private UiTicker RunTicker => field ??= new(_services.Time, _services.Dispatcher, TimeSpan.FromSeconds(1), () => RunningRun?.Tick());
    private ProjectRunViewModel? _notifiedRun;

    internal ProjectRunsViewModel(AppServices services, IProjectRunsHost host)
    {
        _services = services;
        _host = host;
    }

    /// <summary>
    /// Each job this tab has run, newest last (DESIGN.md §18): listed under the tab's row in the sidebar, each with its
    /// own log, until the user closes it. They aren't saved.
    /// </summary>
    public ObservableCollection<ProjectRunViewModel> Items { get; } = [];

    public bool HasRuns => Items.Count > 0;

    /// <summary>The run whose job is running. One job runs at a time per tab.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsJobRunning))]
    public partial ProjectRunViewModel? RunningRun { get; private set; }

    partial void OnRunningRunChanged(ProjectRunViewModel? value)
    {
        RunTicker.Run(RunningRun is not null);
        _host.RunningRunChanged();
    }

    public bool IsJobRunning => RunningRun is not null;

    /// <summary>The folder of the tab's group, which its runs' logs are in too.</summary>
    internal string GroupFolder => _host.GroupFolder;

    /// <summary>While a job runs, other jobs wait: one at a time per tab.</summary>
    internal ProjectAction ForJob(ProjectAction action) =>
        RunningRun is { } running && action.IsEnabled && action.Kind is ProjectActionKind.Run or ProjectActionKind.Destructive
            ? action with { DisabledReason = $"{running.Name} is still running. Stop it first, from its entry in the sidebar or over its log." }
            : action;

    /// <summary>A click on a run's entry in the sidebar: its log shows in the main area.</summary>
    internal void OpenRun(ProjectRunViewModel run)
    {
        if (Items.Contains(run))
        {
            _host.ShowRun(run);
        }
    }

    /// <summary><b>Show output…</b> in the project's menu: the newest run's log, in the main area.</summary>
    internal void ShowNewest()
    {
        if (Items.LastOrDefault() is { } run)
        {
            _host.ShowRun(run);
        }
    }

    /// <summary>A clicked "project action finished" notification: the log of the run it was about, when it's still there.</summary>
    internal void OpenNotifiedRun()
    {
        if (_notifiedRun is { } run && Items.Contains(run))
        {
            _host.ShowRun(run);
        }
    }

    /// <summary>A run's log took the focus, or the focus in its pane: its notification is no longer news.</summary>
    internal void Selected(ProjectRunViewModel run)
    {
        if (ReferenceEquals(_notifiedRun, run))
        {
            _services.Notifications.ClearTab(_host.Id, NotificationKind.ProjectAction);
        }
        _host.RunSelected(run);
    }

    internal void Show(ProjectRunViewModel run) => _host.ShowRun(run);

    internal void ClosePane(ProjectRunViewModel run) => _host.CloseRunPane(run);

    /// <summary>
    /// The × on a finished run's entry, or over its log: the entry and its log go. A running one can't be closed, only
    /// stopped. While its log had the focus, the tab takes its place.
    /// </summary>
    internal void CloseRun(ProjectRunViewModel run)
    {
        if (run.IsRunning || !Items.Remove(run))
        {
            return;
        }
        if (ReferenceEquals(_notifiedRun, run))
        {
            // Its notification is about a log that's gone.
            _notifiedRun = null;
            _services.Notifications.ClearTab(_host.Id, NotificationKind.ProjectAction);
        }
        OnPropertyChanged(nameof(HasRuns));
        _host.RunRemoved(run);
    }

    /// <summary>A new run, under the tab's row. Its log shows when it's clicked; starting doesn't take the main area.</summary>
    private ProjectRunViewModel AddRun(string name, ProjectAction action, string? folder)
    {
        var run = new ProjectRunViewModel(this, name, action, folder ?? _host.Folder, _services.Time);
        Items.Add(run);
        OnPropertyChanged(nameof(HasRuns));
        _host.RunAdded(run);
        return run;
    }

    /// <summary><b>Copy</b> over a run's log: all of it.</summary>
    internal Task CopyAsync(string text) => _services.Platform.SetClipboardTextAsync(text);

    /// <summary>Where a path in <paramref name="run"/>'s log is: from the folder it ran in, then the tab's. Names alone aren't looked for.</summary>
    internal ReplyFileFinder FinderFor(ProjectRunViewModel run) =>
        new(() => run.Folder, () => [_host.Folder], Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), () => null);

    /// <summary>Runs <paramref name="action"/> on the UI thread, for what a lookup off it found.</summary>
    internal void Post(Action action) => _services.Dispatcher.Post(action);

    /// <summary>A click on a link in <paramref name="run"/>'s log (DESIGN.md §18, "Project tools", "A run's log").</summary>
    internal async Task OpenLinkAsync(ProjectRunViewModel run, RunLogLink link)
    {
        if (link.Url is { } url)
        {
            await _services.Platform.OpenUrlAsync(url.OriginalString);
            return;
        }
        if (link.File is not { } file)
        {
            return;
        }
        string? found;
        try
        {
            found = (await run.Finder.FindAsync(file, [], clicked: true)).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            found = null;
        }
        if (found is null || await _host.OpenPathAsync(found, file.Line, file.Column) == OpenedPath.NotFound)
        {
            _host.AddNote($"Couldn't find {file.Path}.", NoteKind.Warning);
        }
    }

    /// <summary>Starts a <see cref="ProjectActionKind.Run"/> action's process as a job, or makes a run that says why it couldn't.</summary>
    internal void StartJob(ProjectAction action, ProcessStartSpec spec)
    {
        ProjectJob job;
        try
        {
            if (action.ResultFile is { } result)
            {
                // A fresh result each time, so the summary never reads an old one.
                Directory.CreateDirectory(Path.GetDirectoryName(result)!);
                File.Delete(result);
            }
            job = _services.ProjectTools.StartJob(action.Label, spec, action.ExtraEnvironment);
        }
        catch (Exception ex) when (ProjectToolsViewModel.IsStartFailure(ex))
        {
            // A run all the same, so its entry and log say why.
            var failed = AddRun(action.Label, action, spec.WorkingDirectory);
            failed.Append([$"$ {action.CommandText}", $"Couldn't start it: {ex.Message}"]);
            failed.EndCouldntStart($"{action.Label} couldn't start: {ex.Message}");
            _host.AddNote($"Couldn't start {action.Label}: {ex.Message}", NoteKind.Error);
            return;
        }
        BeginJob(job, action, $"$ {action.CommandText}", spec.WorkingDirectory);
    }

    /// <summary>
    /// Runs <paramref name="job"/> as the tab's running job, with a new run whose log starts with
    /// <paramref name="firstLine"/>. <paramref name="folder"/> is where it runs; the tab's folder when it doesn't say.
    /// </summary>
    internal void BeginJob(ProjectJob job, ProjectAction action, string firstLine, string? folder = null)
    {
        var run = AddRun(job.Name, action, folder);
        run.Job = job;
        run.Append([firstLine]);
        RunningRun = run;
        if (job.ProcessId is { } pid && _services.ProcessTrees is { } trees)
        {
            try
            {
                var tree = trees.Find(pid) ?? trees.Track(pid);
                Volatile.Write(ref _jobTree, tree);
                job.UseTreeKiller(tree.KillAll);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception or ArgumentException)
            {
                // Without a tree, Stop still ends the process and its children.
            }
        }
        // Each run keeps its own lines, whether or not its log shows.
        job.Output += lines => run.Receive(lines, _services.Dispatcher);
        _ = WatchJobAsync(run, job);
        job.Begin();
        _host.JobBegan();
    }

    private async Task WatchJobAsync(ProjectRunViewModel run, ProjectJob job)
    {
        var result = await job.Completion.ConfigureAwait(false);
        _services.Dispatcher.Post(() => OnJobEnded(run, job, result));
    }

    private void OnJobEnded(ProjectRunViewModel run, ProjectJob job, ProjectJobResult result)
    {
        if (ReferenceEquals(RunningRun, run))
        {
            if (Interlocked.Exchange(ref _jobTree, null) is { } tree)
            {
                tree.Dispose();
            }
            RunningRun = null;
        }
        var action = run.Action;
        var summary = result.State == ProjectJobState.Succeeded || result.ExitCode is not null
            ? action?.Summarize?.Invoke(result.ExitCode ?? 0)
            : null;
        var exit = result.ExitCode is { } code ? $" (exit code {code.ToString(CultureInfo.InvariantCulture)})" : "";
        var status = result.State switch
        {
            ProjectJobState.Succeeded => $"{job.Name} succeeded.",
            ProjectJobState.Stopped => $"{job.Name} was stopped.",
            _ => $"{job.Name} failed{exit}.{(result.Message is { } why ? $" {why}" : "")}",
        } + (summary is null ? "" : $" {summary}");
        // The entry stays, whatever the result, until the user closes it.
        run.End(result.State, result.ExitCode, status);
        if (!Items.Contains(run))
        {
            // The tab closed and left it running.
            return;
        }
        if (result.State != ProjectJobState.Stopped
            && _services.Notifications.Notify(NotificationKind.ProjectAction, _host.DisplayName, status, _host.Id))
        {
            // Only when Claudette isn't in front (DESIGN.md §10); a click shows this run's log.
            _notifiedRun = run;
        }
        _host.JobEnded(result.State == ProjectJobState.Succeeded ? action?.ThenOnSuccess : null);
    }

    /// <summary>The job's processes, for the process monitor: the job's own process isn't the tab's <c>claude</c>.</summary>
    internal IReadOnlyList<ProcessSnapshot> JobProcesses(bool includeCommandLines)
    {
        if (Volatile.Read(ref _jobTree) is not { IsDisposed: false } tree)
        {
            return [];
        }
        try
        {
            return tree.Sample(includeCommandLines).Select(s => s.IsRoot ? s with { IsRoot = false } : s).ToArray();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception or ObjectDisposedException)
        {
            return [];
        }
    }

    /// <summary>Whether <paramref name="pid"/> belongs to the running job, so the process monitor's Stop goes to its tree.</summary>
    internal ProcessTree? JobTreeHolding(int pid) =>
        Volatile.Read(ref _jobTree) is { IsDisposed: false } tree && JobProcesses(false).Any(p => p.Pid == pid) ? tree : null;

    /// <summary>
    /// Closing the tab: a running job is stopped with the tab's other processes, unless they're kept, and the runs and
    /// their logs go with the tab.
    /// </summary>
    internal void CloseRuns(bool killProcesses)
    {
        if (killProcesses)
        {
            RunningRun?.Job?.Stop();
        }
        RunTicker.Stop();
        _notifiedRun = null;
        var runs = Items.ToArray();
        Items.Clear();
        OnPropertyChanged(nameof(HasRuns));
        foreach (var run in runs)
        {
            _host.RunRemoved(run);
        }
    }
}
