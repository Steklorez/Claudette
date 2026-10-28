using Avalonia;

namespace Claudette.App.ViewModels.ToolWindows;

/// <summary>
/// Where a dragged tool window's tab would go (DESIGN.md §3, "Tool windows", "Moving them"): the other edge, beside the
/// windows there or docking it if it has none, marked as a tab dragged over the panes is, with what it would do.
/// </summary>
/// <param name="Edge">The edge it would dock at.</param>
/// <param name="Area">The area it would take, inset as the panes' drop marker is.</param>
/// <param name="Label">What the marker's pill says, such as <i>Dock bottom</i> or <i>Tab with Changed files</i>.</param>
public sealed record ToolDrop(ToolEdge Edge, Rect Area, string Label);

/// <summary>
/// Where the dock's parts are, in the coordinates of the view that holds them: the panes, the whole dock (the panes with
/// the edges), and each edge's docked group or tabs while it shows.
/// </summary>
public sealed record ToolDockGeometry(Rect Panes, Rect Area, Rect? RightGroup = null, Rect? BottomGroup = null, Rect? RightStrip = null, Rect? BottomStrip = null);

public sealed partial class ToolDockViewModel
{
    /// <summary>How far in the drop marker is from the area it marks, as the panes' is.</summary>
    public const double DropInset = 8;

    /// <summary>
    /// Where <paramref name="window"/>'s tab, dragged to <paramref name="at"/>, would go: over the other edge's windows or
    /// tabs, or over the panes nearer that edge than the middle, the other edge. Null anywhere else, and over its own edge,
    /// where dragging along the tabs moves it among them instead.
    /// </summary>
    public ToolDrop? DropAt(ToolWindowViewModel window, Point at, ToolDockGeometry geometry)
    {
        if (EdgeUnder(at, geometry) is not { } to || to == window.Edge)
        {
            return null;
        }
        var edge = EdgeOf(to);
        var others = edge.Tabs.Where(t => !ReferenceEquals(t, window)).ToList();
        if (others.Count == 0)
        {
            return new ToolDrop(to, DockedArea(to, geometry).Deflate(DropInset), to == ToolEdge.Right ? "Dock right" : "Dock bottom");
        }
        var with = edge.Selected is { } selected && others.Contains(selected) ? selected : others[0];
        var group = edge.IsDocked ? (to == ToolEdge.Right ? geometry.RightGroup : geometry.BottomGroup) : null;
        return new ToolDrop(to, (group ?? DockedArea(to, geometry)).Deflate(DropInset), $"Tab with {with.Title}");
    }

    /// <summary>A tab dropped where <see cref="DropAt"/> said: it goes last on that edge, and is the tab it shows.</summary>
    public void Drop(ToolWindowViewModel window, ToolDrop drop) => MoveToEdge(window, drop.Edge);

    /// <summary>
    /// The edge the pointer is over: its docked group or its tabs, or a band of the panes along it, as deep as the edge's
    /// size and no more than a third of the panes; in a corner, the edge it's nearer for its band's depth.
    /// </summary>
    private ToolEdge? EdgeUnder(Point at, ToolDockGeometry geometry)
    {
        if (geometry.RightGroup is { } rightGroup && rightGroup.Contains(at) || geometry.RightStrip is { } rightStrip && rightStrip.Contains(at))
        {
            return ToolEdge.Right;
        }
        if (geometry.BottomGroup is { } bottomGroup && bottomGroup.Contains(at) || geometry.BottomStrip is { } bottomStrip && bottomStrip.Contains(at))
        {
            return ToolEdge.Bottom;
        }
        var panes = geometry.Panes;
        if (!panes.Contains(at) || panes.Width <= 0 || panes.Height <= 0)
        {
            return null;
        }
        var toRight = (panes.Right - at.X) / Math.Min(Right.Size, panes.Width / 3);
        var toBottom = (panes.Bottom - at.Y) / Math.Min(Bottom.Size, panes.Height / 3);
        return toRight <= 1 && toRight <= toBottom ? ToolEdge.Right
            : toBottom <= 1 ? ToolEdge.Bottom
            : null;
    }

    /// <summary>
    /// The area an edge with no windows would take, docked: the right one the whole height at the panes' right, the bottom
    /// one under them, each its size, and no more than half the panes.
    /// </summary>
    private Rect DockedArea(ToolEdge edge, ToolDockGeometry geometry)
    {
        var panes = geometry.Panes;
        if (edge == ToolEdge.Right)
        {
            var width = Math.Min(Right.Size, panes.Width / 2);
            return new Rect(panes.Right - width, geometry.Area.Top, width, geometry.Area.Height);
        }
        var height = Math.Min(Bottom.Size, panes.Height / 2);
        return new Rect(panes.Left, panes.Bottom - height, panes.Width, height);
    }
}
