using System.ComponentModel;

namespace Claudette.App.ViewModels.ToolWindows;

/// <summary>
/// A group of icons on the tool window strip at the window's right (DESIGN.md §3, "Tool windows"), as Rider's stripes
/// have them. One window of each group shows at a time: the top group's at the right, beside the strip; the split group's
/// under it, sharing the right side; the bottom group's under the panes.
/// </summary>
public enum ToolGroup
{
    Top,
    Split,
    Bottom,
}

/// <summary>
/// A kind of tool window (DESIGN.md §3, "Tool windows"): what its icon and header say, the group it starts in until the
/// user moves it, and whether it shows the focused session tab's data. The kinds are <see cref="ToolWindowKinds"/>; each
/// kind's view is in <c>Views/ToolWindowView</c>.
/// </summary>
public sealed class ToolWindowKind
{
    /// <summary>
    /// Its name, which the layout is saved by. The side panel's pages keep the names they had (<c>Files</c>,
    /// <c>Agents</c> and so on), so their order carries over.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>What its header and its icon's tip say.</summary>
    public required string Title { get; init; }

    /// <summary>Its icon on the strip: a geometry's key in the app's resources, such as <c>IconChangedFiles</c>.</summary>
    public required string Icon { get; init; }

    /// <summary>The command palette's entry that shows it, such as <i>Show changed files</i>; null for none.</summary>
    public string? PaletteText { get; init; }

    /// <summary>The group its icon is in until the user moves it.</summary>
    public ToolGroup DefaultGroup { get; init; } = ToolGroup.Top;

    /// <summary>
    /// Its window shows a session tab's data: the focused tab's, or with a Project Files tab or a log focused, its group's
    /// last focused session tab's. The window's <see cref="ToolWindowViewModel.Content"/> is that tab.
    /// </summary>
    public bool FollowsSession { get; init; }

    /// <summary>
    /// For a kind that follows the session: when the session has something for it, such as Processes while the process
    /// monitor is on. Its icon hides the rest of the time. Null: always.
    /// </summary>
    public ToolCondition? ShowsWhen { get; init; }
}

/// <summary>
/// When a kind that follows the session has something to show (<see cref="ToolWindowKind.ShowsWhen"/>):
/// <paramref name="Holds"/>, asked again each time <paramref name="Source"/>'s <paramref name="Property"/> changes.
/// </summary>
public sealed record ToolCondition(Func<TabViewModel, INotifyPropertyChanged> Source, string Property, Func<TabViewModel, bool> Holds);
