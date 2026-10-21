using System.Globalization;
using Claudette.App.Conversation;
using Claudette.App.Services;
using Claudette.Core;
using Claudette.Core.ProjectTools;
using Claudette.Core.ProjectTools.EditorLogs;
using Claudette.Core.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// One run of a project action as a job (DESIGN.md §18, "Project tools"): a build, project file generation, a custom
/// command, Clean's deletion, or one that couldn't start. It keeps its own log and stays in the sidebar under its tab,
/// whatever the result, until the user closes it or the tab. Its log shows in the main area, as a tab of the panes
/// without a composer, so it can sit beside the conversation (<see cref="LogTabViewModel"/>).
/// </summary>
public sealed partial class ProjectRunViewModel : LogTabViewModel
{
    /// <summary>A run keeps at most this many lines of its output.</summary>
    public const int MaxOutputLines = 5000;

    /// <summary>How many of the oldest lines go at a time once the output is at <see cref="MaxOutputLines"/>.</summary>
    public const int DroppedTogether = 500;

    private readonly ProjectRunsViewModel _runs;
    private readonly TimeProvider _time;
    private bool _couldntStart;

    internal ProjectRunViewModel(ProjectRunsViewModel runs, string name, ProjectAction? action, string folder, TimeProvider time)
        : base(MaxOutputLines, DroppedTogether, "run", runs.WordWrap)
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
    public override string Folder { get; }

    // ---- In the panes (DESIGN.md §18, "Split panes") -----------------------------------------------------------------

    public override string DisplayName => Name;

    public override string GroupFolder => _runs.GroupFolder;

    protected override void OnSelected() => _runs.Selected(this);

    protected override void Show() => _runs.Show(this);

    protected override void OnClosePane() => _runs.ClosePane(this);

    protected override Task CopyAsync(string text) => _runs.CopyAsync(text);

    protected override void RememberWordWrap(bool wrap) => _runs.RememberWordWrap(wrap);

    /// <summary>Lines of its output, which have nothing to say of them but their text.</summary>
    internal void Append(IReadOnlyList<string> lines) => Append(lines.Select(LogLine.Plain).ToArray());

    protected override ReplyFileFinder CreateFinder() => _runs.FinderFor(this);

    protected override void Post(Action action) => _runs.Post(action);

    protected override Task FollowLinkAsync(RunLogLink link) => _runs.OpenLinkAsync(this, link);

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

    private bool CanClose => !IsRunning;

    /// <summary>Takes the entry and its log away. A running one has Stop instead, so a stray click never ends a build.</summary>
    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close() => _runs.CloseRun(this);

    public override string ToString() => $"{Name}: {Status}";
}
