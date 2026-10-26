using System.Globalization;
using Claudette.Core.ProjectTools.EditorLogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// A log in the main area, as a tab of the panes without a composer (DESIGN.md §18, "Project tools"): a project run's
/// (<see cref="ProjectRunViewModel"/>) or an editor's (<see cref="EditorLogViewModel"/>). Its entry is under a tab's row
/// in the sidebar. It keeps its last lines, finds the links in them (<c>.Links.cs</c>), and can sit beside the
/// conversation (<see cref="IShellTab"/>).
/// </summary>
public abstract partial class LogTabViewModel : ObservableObject, IShellTab
{
    /// <summary>Set once its constructor has given it its starting values, which aren't choices to remember.</summary>
    private readonly bool _constructed;

    /// <param name="maxLines">It keeps at most this many lines.</param>
    /// <param name="droppedTogether">How many of the oldest go at a time once it's at <paramref name="maxLines"/>.</param>
    /// <param name="idPrefix">The start of its <see cref="Id"/>, "run" or "log".</param>
    /// <param name="wordWrap">It wraps its long lines, as the last log's <b>Wrap lines</b> was left.</param>
    protected LogTabViewModel(int maxLines, int droppedTogether, string idPrefix, bool wordWrap)
    {
        MaxLines = maxLines;
        LinesDroppedAtATime = droppedTogether;
        Id = $"{idPrefix}-{Guid.NewGuid():N}";
        WordWrap = wordWrap;
        _constructed = true;
    }

    public int MaxLines { get; }

    public int LinesDroppedAtATime { get; }

    /// <summary>Its own, for the panes; logs aren't saved, so the app state keeps their tab's instead.</summary>
    public string Id { get; }

    public abstract string DisplayName { get; }

    public abstract string GroupFolder { get; }

    /// <summary>The folder a relative path in it is read against first, before its tab's.</summary>
    public abstract string Folder { get; }

    // ---- In the panes (DESIGN.md §18, "Split panes") -----------------------------------------------------------------

    /// <summary>It has the focus: the keyboard follows it, and its sidebar entry is picked out.</summary>
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
            OnSelected();
        }
    }

    /// <summary>It's on screen, in a pane.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    partial void OnIsShownChanged(bool value) => OnShownChanged(value);

    [ObservableProperty]
    public partial bool IsInSplit { get; set; }

    [ObservableProperty]
    public partial int PaneIndex { get; set; } = -1;

    /// <summary>Its group's color, for its pane's header with tabs side by side (DESIGN.md §18, "Split panes"); the shell keeps it.</summary>
    [ObservableProperty]
    public partial Avalonia.Media.Color? PaneColor { get; set; }

    [ObservableProperty]
    public partial bool IsDragged { get; set; }

    /// <summary>It has no side panel.</summary>
    public bool ShowsSidePanel => false;

    /// <summary>A click or the focus in its pane: the keyboard follows it now.</summary>
    public void FocusPane()
    {
        if (IsShown && !IsSelected)
        {
            Show();
        }
    }

    /// <summary>It took the focus.</summary>
    protected abstract void OnSelected();

    /// <summary>It came on screen, or left it.</summary>
    protected virtual void OnShownChanged(bool shown)
    {
    }

    /// <summary>It takes the focus, in the focused pane or where it's on screen already.</summary>
    protected abstract void Show();

    /// <summary><b>Close pane</b> on its pane's header, and in its entry's menu: it stays in the sidebar.</summary>
    [RelayCommand]
    private void ClosePane() => OnClosePane();

    protected abstract void OnClosePane();

    /// <summary>Find in the log (<c>Ctrl/Cmd+F</c>), for its view to open.</summary>
    public event Action? FindRequested;

    internal void RequestFind() => FindRequested?.Invoke();

    // ---- How it's shown -----------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Stay current</b>: it follows its newest line, as a terminal does. Scrolling up from the end turns it off, and
    /// scrolling back to the end, or turning it on, goes to the newest line again.
    /// </summary>
    [ObservableProperty]
    public partial bool StayCurrent { get; set; } = true;

    /// <summary><b>Wrap lines</b>: its long lines wrap at the width shown; otherwise it scrolls sideways.</summary>
    [ObservableProperty]
    public partial bool WordWrap { get; set; }

    partial void OnWordWrapChanged(bool value)
    {
        if (_constructed)
        {
            RememberWordWrap(value);
        }
    }

    public static string StayCurrentTip => "Keep the newest line in view: scrolling up turns it off, and scrolling back to the end turns it on";

    public static string WordWrapTip => "Wrap long lines at the width shown; off, the log scrolls sideways";

    /// <summary>The line at the top of its view while it isn't staying current, for the view to come back to.</summary>
    internal LogLine? TopLine { get; set; }

    /// <summary>The next log starts with <b>Wrap lines</b> as this one was left.</summary>
    protected abstract void RememberWordWrap(bool wrap);

    /// <summary>The line is shown, as its filter says; a run's are all shown.</summary>
    public virtual bool Shows(LogLine line) => true;

    /// <summary>Its filter changed, so the lines shown are others.</summary>
    public event Action? FilterChanged;

    protected void OnFilterChanged() => FilterChanged?.Invoke();

    // ---- Its lines ---------------------------------------------------------------------------------------------------

    /// <summary>Its lines, at most <see cref="MaxLines"/>.</summary>
    public BatchedCollection<LogLine> Output { get; } = [];

    /// <summary>Lines dropped from the start to keep it to the limit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputNote))]
    public partial int OutputDropped { get; private set; }

    public virtual string? OutputNote => OutputDropped > 0
        ? $"Keeping at most the last {MaxLines.ToString("N0", CultureInfo.CurrentCulture)} lines; {OutputDropped.ToString("N0", CultureInfo.CurrentCulture)} earlier ones were dropped."
        : null;

    /// <summary>A batch of lines, with one change to the list for the whole batch.</summary>
    internal void Append(IReadOnlyList<LogLine> lines)
    {
        if (Output.AddAndTrim(lines, MaxLines, LinesDroppedAtATime) is > 0 and var dropped)
        {
            OutputDropped += dropped;
        }
    }

    /// <summary>Its lines afresh: <paramref name="lines"/> in place of all it had, as one change to the list.</summary>
    internal void Replace(IReadOnlyList<LogLine> lines)
    {
        Output.Clear();
        OutputDropped = 0;
        Append(lines);
    }

    /// <summary><b>Copy</b> over it: the lines shown, as the file has them.</summary>
    [RelayCommand]
    private Task CopyOutputAsync() => CopyAsync(string.Join(Environment.NewLine, Output.Where(Shows).Select(l => l.Text)));

    protected abstract Task CopyAsync(string text);
}
