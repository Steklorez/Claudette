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

/// <summary>What an editor's log needs from the shell, which puts it under one of its project's tabs.</summary>
internal interface IEditorLogHost
{
    /// <summary>The tab it's under, for the folder its paths are read against after the project's, and its notes.</summary>
    TabViewModel? OwnerOf(EditorLogViewModel log);

    void ShowLog(EditorLogViewModel log);

    void LogSelected(EditorLogViewModel log);

    void CloseLogPane(EditorLogViewModel log);

    /// <summary>× on its row or over it: it goes until something next writes its file.</summary>
    void DismissLog(EditorLogViewModel log);
}

/// <summary>
/// The log one of the project's engine processes writes, such as the Unreal editor's <c>Saved/Logs/NightOwl.log</c>
/// (DESIGN.md §18, "Editor logs"). It appears under one of the project's tabs while its process runs, and stays after it
/// ends; its lines, read by <see cref="EditorLogService"/>, show in the main area as a run's log does, colored by how
/// bad each is, with the errors and warnings counted on its row. When something writes the file again, it shows the new
/// log in place of the old.
/// </summary>
public sealed partial class EditorLogViewModel : LogTabViewModel
{
    /// <summary>An editor's log keeps at most this many of its lines; the rest are in its file.</summary>
    public const int MaxLogLines = 20_000;

    /// <summary>How many of the oldest lines go at a time once it's at <see cref="MaxLogLines"/>.</summary>
    public const int DroppedLogLines = 2_000;

    private readonly AppServices _services;
    private readonly IEditorLogHost _host;
    private readonly EditorLogSource _source;
    private IReadOnlyList<LogWriterProcess> _writers = [];
    private bool _earlierInFile;

    internal EditorLogViewModel(AppServices services, IEditorLogHost host, string path, FollowedProject project)
        : base(MaxLogLines, DroppedLogLines, "log", services.State.LogWordWrap)
    {
        _services = services;
        _host = host;
        _source = project.Source;
        LogPath = path;
        ProjectPath = project.ProjectPath;
        Root = project.Root;
        FileName = System.IO.Path.GetFileName(path);
        Started = services.Time.GetUtcNow();
        CreateFilter();
    }

    /// <summary>The log's file.</summary>
    public string LogPath { get; }

    /// <summary>"NightOwl.log".</summary>
    public string FileName { get; }

    /// <summary>The project it's the log of, by its <see cref="ProjectInfo.ProjectPath"/>.</summary>
    public string ProjectPath { get; }

    /// <summary>The project's folder.</summary>
    public string Root { get; }

    public override string DisplayName => FileName;

    public override string GroupFolder => Owner?.GroupFolder ?? Root;

    /// <summary>A relative path in it is read against the project's folder, then its tab's.</summary>
    public override string Folder => Root;

    /// <summary>×'d away while its process ran: it stays away until something next writes its file.</summary>
    internal bool IsDismissed { get; set; }

    /// <summary>The tab whose process or launch started it: it goes under that one, while that one has the project.</summary>
    internal TabViewModel? PreferredTab { get; set; }

    /// <summary>The tab it's under now; null while it's under none.</summary>
    internal TabViewModel? Owner { get; set; }

    // ---- How it's going ----------------------------------------------------------------------------------------------

    /// <summary>One of the project's processes is writing it now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail), nameof(Status), nameof(Tip), nameof(Timing))]
    public partial bool IsRunning { get; private set; }

    /// <summary>Its log says its process crashed, such as Unreal's <c>=== Critical error: ===</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail), nameof(Status), nameof(Tip), nameof(IsError))]
    public partial bool IsCrashed { get; private set; }

    /// <summary>What writes it: "Unreal editor", "Unreal commandlet ResavePackages", "Unity (batch mode)".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail), nameof(Status), nameof(Tip))]
    public partial string Role { get; private set; } = "";

    /// <summary>When its process started, or when Claudette first saw it, if that can't be read.</summary>
    public DateTimeOffset Started { get; private set; }

    /// <summary>When its process was seen to have ended; null while it runs.</summary>
    public DateTimeOffset? Ended { get; private set; }

    /// <summary>The error lines in its log, those Claudette no longer keeps included.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail), nameof(Counts))]
    public partial int Errors { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail), nameof(Counts))]
    public partial int Warnings { get; private set; }

    /// <summary>Errors that came while its log wasn't on screen: its row's tab, collapsed, says so.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsError))]
    public partial int UnseenErrors { get; private set; }

    /// <summary>Its row and the collapsed tab's badge show an error: it crashed, or errors came since it was last looked at.</summary>
    public bool IsError => IsCrashed || UnseenErrors > 0;

    /// <summary>"2 errors · 14 warnings"; empty when it has neither.</summary>
    public string Counts
    {
        get
        {
            var parts = new List<string>(2);
            if (Errors > 0)
            {
                parts.Add(Errors == 1 ? "1 error" : $"{Errors.ToString("N0", CultureInfo.CurrentCulture)} errors");
            }
            if (Warnings > 0)
            {
                parts.Add(Warnings == 1 ? "1 warning" : $"{Warnings.ToString("N0", CultureInfo.CurrentCulture)} warnings");
            }
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Its row's second line: what writes it while it runs, or how it ended and when, then the counts:
    /// "Unreal editor · 2 errors · 14 warnings", "Closed · 15:02 · 1 error", "Crashed · 15:02".
    /// </summary>
    public string Detail
    {
        get
        {
            var state = IsRunning && !IsCrashed ? Role
                : $"{(IsCrashed ? "Crashed" : "Closed")}{(Ended is { } ended ? $" · {MessageTimes.Short(ended, _services.Time)}" : "")}";
            return Counts is { Length: > 0 } counts ? $"{state} · {counts}" : state;
        }
    }

    /// <summary>
    /// The line over its log: "Unreal editor running since 14:30 (PID 4412).", "Unreal editor closed at 15:02.",
    /// "Unreal editor crashed at 15:02."
    /// </summary>
    public string Status
    {
        get
        {
            var role = Role.Length > 0 ? Role : "Its process";
            if (IsRunning && !IsCrashed)
            {
                var pids = _writers.Select(w => w.Process.Pid.ToString(CultureInfo.InvariantCulture)).ToArray();
                var pid = pids.Length switch
                {
                    0 => "",
                    1 => $" (PID {pids[0]})",
                    _ => $" (PIDs {string.Join(", ", pids)})",
                };
                return $"{role} running since {MessageTimes.Short(Started, _services.Time)}{pid}.";
            }
            var how = IsCrashed ? "crashed" : "closed";
            return Ended is { } ended ? $"{role} {how} at {MessageTimes.Short(ended, _services.Time)}." : $"{role} {how}.";
        }
    }

    /// <summary>Once its process has ended, when it started and how long it ran: "Started 14:30 · ran 32m 10s". Null while it runs, which the status says.</summary>
    public string? Timing => Ended is { } ended
        ? $"Started {MessageTimes.Short(Started, _services.Time)} · ran {Formats.Elapsed(ended - Started)}"
        : null;

    /// <summary>Its row's tip: the status line and the file.</summary>
    public string Tip => $"{Status}\n{LogPath}";

    /// <summary>The × button's tip, on its row and over its log.</summary>
    public static string CloseTip => "Close: it comes back when its log starts again, as when the editor does";

    /// <summary><b>Show in Explorer</b>, <b>Show in Finder</b> or <b>Show in File Manager</b>.</summary>
    public static string ShowFolderLabel => ProjectMenu.ShowFolderLabel(ToolOSExtensions.Current);

    public override string? OutputNote => _earlierInFile || OutputDropped > 0
        ? $"Showing the last {Output.Count.ToString("N0", CultureInfo.CurrentCulture)} lines; the earlier ones are in the file."
        : null;

    // ---- What the logs' service found (DESIGN.md §18, "Editor logs") ------------------------------------------------

    /// <summary>A check found it being written by <paramref name="writers"/>; <paramref name="newWriters"/> when they've just started.</summary>
    internal void Writing(IReadOnlyList<LogWriterProcess> writers, bool newWriters)
    {
        _writers = writers;
        if (newWriters || !IsRunning)
        {
            Started = writers.Select(w => w.Process.StartedAt).OfType<DateTimeOffset>().DefaultIfEmpty(_services.Time.GetUtcNow()).Min();
            Ended = null;
            IsCrashed = false;
        }
        Role = writers.Count > 0 ? writers[0].Role : Role;
        IsRunning = true;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Timing));
    }

    /// <summary>A check found nothing writing it any more.</summary>
    internal void Stopped()
    {
        if (!IsRunning)
        {
            return;
        }
        _writers = [];
        Ended = _services.Time.GetUtcNow();
        IsRunning = false;
        OnPropertyChanged(nameof(Timing));
    }

    /// <summary>A read found new lines, or the log afresh.</summary>
    internal void Receive(EditorLogRead read)
    {
        if (read.Fresh)
        {
            _earlierInFile = read.Skipped;
            Errors = 0;
            Warnings = 0;
            UnseenErrors = 0;
            IsCrashed = false;
            Replace(read.Lines);
        }
        else
        {
            Append(read.Lines);
        }
        Errors += read.Errors;
        Warnings += read.Warnings;
        if (!IsShown)
        {
            UnseenErrors += read.Errors;
        }
        if (read.Crashed)
        {
            IsCrashed = true;
        }
        CountEntries(read);
        OnPropertyChanged(nameof(OutputNote));
    }

    // ---- In the panes, and its row ---------------------------------------------------------------------------------

    protected override void OnSelected() => _host.LogSelected(this);

    protected override void OnShownChanged(bool shown)
    {
        if (shown)
        {
            // Looked at now: its errors are no longer news.
            UnseenErrors = 0;
        }
    }

    protected override void Show() => _host.ShowLog(this);

    protected override void OnClosePane() => _host.CloseLogPane(this);

    protected override Task CopyAsync(string text) => _services.Platform.SetClipboardTextAsync(text);

    protected override void RememberWordWrap(bool wrap)
    {
        if (_services.State.LogWordWrap != wrap)
        {
            _services.State.LogWordWrap = wrap;
            _services.SaveState();
        }
    }

    protected override ReplyFileFinder CreateFinder() =>
        new(() => Root, () => _host.OwnerOf(this) is { } tab ? [tab.Folder] : [], Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), () => null);

    protected override void Post(Action action) => _services.Dispatcher.Post(action);

    protected override async Task FollowLinkAsync(RunLogLink link)
    {
        if (link.Url is { } url)
        {
            await _services.Platform.OpenUrlAsync(url.OriginalString);
            return;
        }
        if (link.File is not { } file || _host.OwnerOf(this) is not { } tab)
        {
            return;
        }
        string? found;
        try
        {
            found = (await Finder.FindAsync(file, [], clicked: true)).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            found = null;
        }
        if (found is null || await tab.OpenPathAsync(found, file.Line, file.Column) == OpenedPath.NotFound)
        {
            ((ITabAreaHost)tab).AddNote($"Couldn't find {file.Path}.", NoteKind.Warning);
        }
    }

    /// <summary>A click on its row: its log takes the focused pane, or the focus where it's on screen already.</summary>
    [RelayCommand]
    private void Open() => _host.ShowLog(this);

    /// <summary>× on its row or over its log: it goes, until something next writes its file.</summary>
    [RelayCommand]
    private void Close() => _host.DismissLog(this);

    /// <summary><b>Open file</b>: the log in Claudette's editor or the OS's app, as Settings → Files and diffs says.</summary>
    [RelayCommand]
    private async Task OpenFileAsync()
    {
        if (_host.OwnerOf(this) is { } tab && await tab.OpenPathAsync(LogPath) == OpenedPath.NotFound)
        {
            ((ITabAreaHost)tab).AddNote($"Couldn't find {LogPath}.", NoteKind.Warning);
        }
    }

    /// <summary><b>Show in Explorer</b> (Finder, the file manager): the log's folder.</summary>
    [RelayCommand]
    private Task ShowFolderAsync() =>
        System.IO.Path.GetDirectoryName(LogPath) is { } folder ? _services.Platform.RevealFolderAsync(folder) : Task.CompletedTask;

    public override string ToString() => $"{FileName}: {Status}";
}
