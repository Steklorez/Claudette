namespace Claudette.Core.Settings;

/// <summary>
/// The tool windows' layout (DESIGN.md §3, "Tool windows"): one for the whole window, kept with the app state. The right
/// side's width, the bottom's height and how the right side is shared while a window shows in each of its groups, and each
/// window's group, place in it, whether it shows and its view mode.
/// </summary>
public sealed class ToolWindowsState
{
    /// <summary>The right side's width, as the user dragged it; null for the default.</summary>
    public double? RightWidth { get; set; }

    /// <summary>The bottom's height, as the user dragged it; null for the default.</summary>
    public double? BottomHeight { get; set; }

    /// <summary>The top window's share of the right side's height while the split group shows one too; null for half.</summary>
    public double? SplitShare { get; set; }

    /// <summary>The windows, by id, in their order along the strip.</summary>
    public List<ToolWindowState> Windows { get; set; } = [];

    /// <summary>
    /// Before the strips, the edges: <c>Right</c> and <c>Bottom</c>, each with its size. Read once, so the sizes carry over;
    /// never written.
    /// </summary>
    public List<ToolEdgeState>? Edges { get; set; }
}

/// <summary>An edge of the window, as the layout before the strips kept it: only its size carries over.</summary>
public sealed class ToolEdgeState
{
    /// <summary><c>Right</c> or <c>Bottom</c>.</summary>
    public string Edge { get; set; } = "";

    /// <summary>The right edge's width or the bottom's height; null for the default.</summary>
    public double? Size { get; set; }
}

/// <summary>A tool window's place on the strip.</summary>
public sealed class ToolWindowState
{
    /// <summary>Its id: its kind's.</summary>
    public string Id { get; set; } = "";

    /// <summary>Its group on the strip: <c>Top</c>, <c>Split</c> or <c>Bottom</c>; null in a layout from before the strips.</summary>
    public string? Group { get; set; }

    /// <summary>
    /// Before the strips, the edge it docked at: <c>Right</c>, which is the top group now, or <c>Bottom</c>. Read when
    /// <see cref="Group"/> isn't there.
    /// </summary>
    public string? Edge { get; set; }

    /// <summary>It's on screen, beside the strip.</summary>
    public bool IsShowing { get; set; }

    /// <summary>Dock Pinned, which stays until it's hidden; false for Dock Unpinned, which hides when the user clicks elsewhere.</summary>
    public bool IsPinned { get; set; } = true;
}
