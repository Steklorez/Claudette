using System.Globalization;
using Claudette.App.Conversation;
using Claudette.App.Services;
using Claudette.Core;
using Claudette.Core.ProjectTools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// One run of a project action as a job (DESIGN.md §18, "Project tools"): a build, project file generation, a custom
/// command, Clean's deletion, or one that couldn't start. It keeps its own log and stays in the sidebar under its tab,
/// whatever the result, until the user closes it or the tab. Its log shows in the main area, as a tab of the panes
/// without a composer, so it can sit beside the conversation (<see cref="IShellTab"/>); the links in it are in
/// <c>.Links.cs</c>.
/// </summary>
public sealed partial class ProjectRunViewModel : ObservableObject, IShellTab
{
    /// <summary>A run keeps at most this many lines of its output.</summary>
    public const int MaxOutputLines = 5000;

    /// <summary>How many of the oldest lines go at a time once the output is at <see cref="MaxOutputLines"/>.</summary>
    public const int DroppedTogether = 500;

    private readonly ProjectRunsViewModel _runs;
    private readonly TimeProvider _time;
    private bool _couldntStart;

    internal ProjectRunViewModel(ProjectRunsViewModel runs, string name, ProjectAction? action, string folder, TimeProvider time)
    {
        _runs = runs;
        _time = time;
        Name = name;
        Action = action;
        Folder = folder;
        Started = time.GetUtcNow();
        Status = $"{name} is running…";
    }

    /// <summary>"Build editor", "Clean intermediates".</summary>
    public string Name { get; }

    /// <summary>The folder it ran in, which a relative path in its log is read against before the tab's.</summary>
    public string Folder { get; }

    // ---- In the panes (DESIGN.md §18, "Split panes") -----------------------------------------------------------------

    /// <summary>Its own, for the panes; runs aren't saved, so the app state keeps its tab's instead.</summary>
    public string Id { get; } = $"run-{Guid.NewGuid():N}";

    public string DisplayName => Name;

    public string GroupFolder => _runs.GroupFolder;

    /// <summary>Its log has the focus: the keyboard follows it, and its sidebar entry is picked out.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (!IsInSplit)
        {
            IsShown = value;
        }
        if (value)
        {
            _runs.Selected(this);
        }
    }

    /// <summary>Its log is on screen, in a pane.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    [ObservableProperty]
    public partial bool IsInSplit { get; set; }

    [ObservableProperty]
    public partial int PaneIndex { get; set; } = -1;

    [ObservableProperty]
    public partial bool IsDragged { get; set; }

    /// <summary>It has no side panel.</summary>
    public bool ShowsSidePanel => false;

    /// <summary>A click or the focus in its pane: the keyboard follows it now.</summary>
    public void FocusPane()
    {
        if (IsShown && !IsSelected)
        {
            _runs.Show(this);
        }
    }

    /// <summary><b>Close pane</b> on its pane's header, and in its entry's menu: the run stays in the sidebar.</summary>
    [RelayCommand]
    private void ClosePane() => _runs.ClosePane(this);

    /// <summary>Find in the log (<c>Ctrl/Cmd+F</c>), for its view to open.</summary>
    public event Action? FindRequested;

    internal void RequestFind() => FindRequested?.Invoke();

    public DateTimeOffset Started { get; }

    public DateTimeOffset? Ended { get; private set; }

    /// <summary>The action it ran, for what follows it (Build and launch) and its summary (test counts).</summary>
    internal ProjectAction? Action { get; }

    /// <summary>The job, for Stop; null for a run that couldn't start.</summary>
    internal ProjectJob? Job { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(Succeeded), nameof(Failed), nameof(IsStopped), nameof(Glyph), nameof(Detail), nameof(Timing), nameof(Tip))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand), nameof(CloseCommand))]
    public partial ProjectJobState State { get; private set; } = ProjectJobState.Running;

    public bool IsRunning => State == ProjectJobState.Running;

    public bool Succeeded => State == ProjectJobState.Succeeded;

    public bool Failed => State == ProjectJobState.Failed;

    public bool IsStopped => State == ProjectJobState.Stopped;

    /// <summary>The process's exit code, once it has ended; null for work Claudette did itself.</summary>
    public int? ExitCode { get; private set; }

    /// <summary>"Build editor is running…", "Build editor succeeded.", "Build editor failed (exit code 6)."</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tip))]
    public partial string Status { get; private set; }

    /// <summary>The sidebar entry's tip: the status line, and when it started and how long it took.</summary>
    public string Tip => State == ProjectJobState.Running ? $"{Status}\nStarted {MessageTimes.Short(Started, _time)}."
        : _couldntStart ? Status
        : $"{Status}\nStarted {MessageTimes.Short(Started, _time)} and took {Formats.Elapsed((Ended ?? Started) - Started)}.";

    /// <summary>The sidebar entry's state: busy while running, then ✓, ✕ or ■.</summary>
    public string Glyph => State switch
    {
        ProjectJobState.Succeeded => "✓",
        ProjectJobState.Failed => "✕",
        ProjectJobState.Stopped => "■",
        _ => "●",
    };

    /// <summary>
    /// The sidebar entry's detail: how long it has been running, or how it ended and when: "Running · 1m 05s",
    /// "Failed · exit code 6 · 14:32".
    /// </summary>
    public string Detail => State == ProjectJobState.Running
        ? $"Running · {Formats.Elapsed(_time.GetUtcNow() - Started)}"
        : $"{Result} · {MessageTimes.Short(Ended ?? Started, _time)}";

    /// <summary>
    /// The line under the status over its log: when it started and how long it has taken, "Started 14:30 · 1m 05s so far"
    /// or "Started 14:30 · took 1m 05s". Null for one that couldn't start.
    /// </summary>
    public string? Timing => _couldntStart ? null
        : State == ProjectJobState.Running ? $"Started {MessageTimes.Short(Started, _time)} · {Formats.Elapsed(_time.GetUtcNow() - Started)} so far"
        : $"Started {MessageTimes.Short(Started, _time)} · took {Formats.Elapsed((Ended ?? Started) - Started)}";

    private string Result => State switch
    {
        ProjectJobState.Succeeded => "Succeeded",
        ProjectJobState.Stopped => "Stopped",
        _ when _couldntStart => "Couldn't start",
        _ => ExitCode is { } code ? $"Failed · exit code {code.ToString(CultureInfo.InvariantCulture)}" : "Failed",
    };

    /// <summary>The run's output, at most <see cref="MaxOutputLines"/> lines.</summary>
    public BatchedCollection<string> Output { get; } = [];

    /// <summary>Lines dropped from the start of the output to keep it to the limit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputNote))]
    public partial int OutputDropped { get; private set; }

    public string? OutputNote => OutputDropped > 0
        ? $"Keeping at most the last {MaxOutputLines.ToString("N0", CultureInfo.CurrentCulture)} lines; {OutputDropped.ToString("N0", CultureInfo.CurrentCulture)} earlier ones were dropped."
        : null;

    /// <summary>A batch of lines, with one change to the list for the whole batch.</summary>
    internal void Append(IReadOnlyList<string> lines)
    {
        if (Output.AddAndTrim(lines, MaxOutputLines, DroppedTogether) is > 0 and var dropped)
        {
            OutputDropped += dropped;
        }
    }

    private readonly Lock _receivedLock = new();

    /// <summary>Lines from the job waiting for the UI thread; not null while they're on their way.</summary>
    private List<string>? _received;

    /// <summary>
    /// Lines from the job, on its thread. While earlier ones wait for the UI thread these join them, so a busy job is a
    /// batch for each time the UI thread gets to it rather than a post for each line.
    /// </summary>
    internal void Receive(IReadOnlyList<string> lines, IUiDispatcher dispatcher)
    {
        lock (_receivedLock)
        {
            if (_received is not null)
            {
                _received.AddRange(lines);
                return;
            }
            _received = [.. lines];
        }
        dispatcher.Post(() =>
        {
            List<string> batch;
            lock (_receivedLock)
            {
                batch = _received!;
                _received = null;
            }
            Append(batch);
        });
    }

    /// <summary>The job ended: its state, exit code and status line, which also ends the log.</summary>
    internal void End(ProjectJobState state, int? exitCode, string status)
    {
        Ended = _time.GetUtcNow();
        ExitCode = exitCode;
        Status = status;
        State = state;
        Append([status]);
    }

    /// <summary>The job's process couldn't start: a failed run whose log says why.</summary>
    internal void EndCouldntStart(string status)
    {
        _couldntStart = true;
        Ended = _time.GetUtcNow();
        Status = status;
        State = ProjectJobState.Failed;
    }

    /// <summary>The running time moved on.</summary>
    internal void Tick()
    {
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(Timing));
    }

    /// <summary>A click on the sidebar entry: its log takes the focused pane, or the focus where it's on screen already.</summary>
    [RelayCommand]
    private void Open() => _runs.OpenRun(this);

    /// <summary>Ends the job's whole process tree, or its work.</summary>
    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop() => Job?.Stop();

    /// <summary><b>Copy</b> over the log: all of it.</summary>
    [RelayCommand]
    private Task CopyOutputAsync() => _runs.CopyOutputAsync(this);

    private bool CanClose => !IsRunning;

    /// <summary>Takes the entry and its log away. A running one has Stop instead, so a stray click never ends a build.</summary>
    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close() => _runs.CloseRun(this);

    public override string ToString() => $"{Name}: {Status}";
}
