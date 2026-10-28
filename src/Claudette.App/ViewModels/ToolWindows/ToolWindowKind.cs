using System.ComponentModel;

namespace Claudette.App.ViewModels.ToolWindows;

/// <summary>
/// An edge of the main window that tool windows dock at (DESIGN.md §3, "Tool windows"): right of the panes, the whole
/// height, or under them.
/// </summary>
public enum ToolEdge
{
    Right,
    Bottom,
}

/// <summary>
/// A kind of tool window (DESIGN.md §3, "Tool windows"): what its tab says, the edge it docks at until the user moves it,
/// and whether it shows the focused session tab's data. The built-in kinds are <see cref="ToolWindowKinds"/>; each kind's
/// view is in <c>Views/ToolWindowView</c>.
/// </summary>
public sealed class ToolWindowKind
{
    /// <summary>
    /// Its name, which the layout is saved by: the window of a kind that has one is saved by it. The side panel's pages
    /// keep the names they had (<c>Files</c>, <c>Agents</c> and so on), so their order carries over.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>What its tab and the Tool windows menu say.</summary>
    public required string Title { get; init; }

    /// <summary>The command palette's entry that shows it, such as <i>Show changed files</i>; null for none.</summary>
    public string? PaletteText { get; init; }

    /// <summary>Where its windows dock until the user moves one.</summary>
    public ToolEdge DefaultEdge { get; init; } = ToolEdge.Right;

    /// <summary>
    /// Its window shows a session tab's data: the focused tab's, or with a Project Files tab or a log focused, its group's
    /// last focused session tab's. The window's <see cref="ToolWindowViewModel.Content"/> is that tab.
    /// </summary>
    public bool FollowsSession { get; init; }

    /// <summary>
    /// For a kind that follows the session: when the session has something for it, such as Processes while the process
    /// monitor is on. Its tab hides the rest of the time. Null: always.
    /// </summary>
    public ToolCondition? ShowsWhen { get; init; }

    /// <summary>
    /// Several windows at once, each made at run time with its own content and title by <see cref="ToolDockViewModel.Add"/>,
    /// and gone for good once closed. A kind that isn't has one window, made with the dock, which keeps its place.
    /// </summary>
    public bool IsMultiple { get; init; }
}

/// <summary>
/// When a kind that follows the session has something to show (<see cref="ToolWindowKind.ShowsWhen"/>):
/// <paramref name="Holds"/>, asked again each time <paramref name="Source"/>'s <paramref name="Property"/> changes.
/// </summary>
public sealed record ToolCondition(Func<TabViewModel, INotifyPropertyChanged> Source, string Property, Func<TabViewModel, bool> Holds);
