using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels.ToolWindows;

/// <summary>
/// One tool window (DESIGN.md §3, "Tool windows"): its tab on an edge, open or closed, and what it shows. The dock keeps
/// its place; its kind says what it is.
/// </summary>
public sealed partial class ToolWindowViewModel : ViewModelBase
{
    private readonly ToolDockViewModel _dock;

    internal ToolWindowViewModel(ToolDockViewModel dock, ToolWindowKind kind, string id, string title, object? content)
    {
        _dock = dock;
        Kind = kind;
        Id = id;
        Title = title;
        Content = content;
        Edge = kind.DefaultEdge;
        // A kind that shows only when the session has something for it waits to be told it has.
        IsAvailable = kind.ShowsWhen is null;
    }

    public ToolWindowKind Kind { get; }

    /// <summary>Its id, which its place is saved by: its kind's, for a kind that has one window.</summary>
    public string Id { get; }

    /// <summary>What its tab says, such as <i>Changed files</i>, or a search's query.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    public partial string Title { get; set; }

    /// <summary>
    /// What its view shows: for a kind that follows the session, the session tab whose data it shows (null with no session
    /// tab); for others, what made it.
    /// </summary>
    [ObservableProperty]
    public partial object? Content { get; set; }

    /// <summary>The edge it docks at.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    public partial ToolEdge Edge { get; internal set; }

    /// <summary>It has a tab on its edge; a closed window comes back where it was.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTab))]
    public partial bool IsOpen { get; internal set; } = true;

    /// <summary>
    /// It has something to show: for a kind that follows the session, as <see cref="ToolWindowKind.ShowsWhen"/> says of the
    /// session (the dock keeps it); for others, as what made it says. Its tab hides the rest of the time.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTab))]
    public partial bool IsAvailable { get; set; }

    partial void OnIsAvailableChanged(bool value) => _dock.Refresh();

    /// <summary>Its tab is on its edge: open, with something to show.</summary>
    public bool HasTab => IsOpen && IsAvailable;

    /// <summary>Its edge's tab showing: the docked group's, while the edge is docked.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; internal set; }

    /// <summary>On screen: its docked group's tab showing, or slid out over the conversation.</summary>
    [ObservableProperty]
    public partial bool IsShowing { get; internal set; }

    /// <summary>Its edge folds its windows into tabs along the window's edge.</summary>
    public bool IsAutoHidden => _dock.EdgeOf(Edge).IsAutoHide;

    /// <summary>What a screen reader hears for its tab, such as <i>Changed files, auto-hidden, right</i>.</summary>
    public string AutomationName => $"{Title}, {(IsAutoHidden ? "auto-hidden" : "docked")}, {(Edge == ToolEdge.Right ? "right" : "bottom")}";

    /// <summary>Its edge was pinned or unpinned: its state, in what a screen reader hears, changed.</summary>
    internal void OnEdgeStateChanged()
    {
        OnPropertyChanged(nameof(IsAutoHidden));
        OnPropertyChanged(nameof(AutomationName));
    }

    /// <summary>Its tab clicked in a docked group: it shows.</summary>
    [RelayCommand]
    private void Select() => _dock.Select(this);

    // ---- Beside its title, as the side panel's tabs had (DESIGN.md §3, "Tool windows") ----------------------------

    /// <summary>A count beside its title, such as Tasks' <i>2 of 5</i>; null for none.</summary>
    [ObservableProperty]
    public partial string? Badge { get; internal set; }

    /// <summary>Its work is running: a subagent, or a project's job. A dot in the busy color.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; internal set; }

    /// <summary>Something needs looking at, such as an MCP server that failed. A dot in the warning color.</summary>
    [ObservableProperty]
    public partial bool HasWarning { get; internal set; }

    /// <summary>Its tab's tip: what it holds now.</summary>
    [ObservableProperty]
    public partial string? Tip { get; internal set; }

    /// <summary>Its tab's menu: <b>Move to the bottom</b>, or <b>Move to the right</b>.</summary>
    public string MoveText => Edge == ToolEdge.Right ? "Move to the bottom" : "Move to the right";

    partial void OnEdgeChanged(ToolEdge value) => OnPropertyChanged(nameof(MoveText));

    /// <summary>Its tab's <b>Move to the bottom</b> or <b>Move to the right</b>: the other edge, where it shows.</summary>
    [RelayCommand]
    private void MoveToOtherEdge() => _dock.MoveToEdge(this, Edge == ToolEdge.Right ? ToolEdge.Bottom : ToolEdge.Right);

    /// <summary>The header's <b>Tool windows</b> menu: shows it, or hides it while it shows.</summary>
    [RelayCommand]
    private void Toggle() => _dock.Toggle(this);

    /// <summary>Its ×, or its tab's <b>Close</b>.</summary>
    [RelayCommand]
    private void Close() => _dock.Close(this);

    public override string ToString() => Title;
}
