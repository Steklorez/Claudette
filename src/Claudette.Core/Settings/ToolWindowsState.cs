namespace Claudette.Core.Settings;

/// <summary>
/// The tool windows' layout (DESIGN.md §3, "Tool windows"): one for the whole window, kept with the app state. Each edge's
/// size, whether it's docked or auto-hidden and the tab it shows, and each window's edge, place along it and whether it's
/// open.
/// </summary>
public sealed class ToolWindowsState
{
    /// <summary>The edges, <c>Right</c> and <c>Bottom</c>.</summary>
    public List<ToolEdgeState> Edges { get; set; } = [];

    /// <summary>The windows that keep a place, by id, in their order along their edge.</summary>
    public List<ToolWindowState> Windows { get; set; } = [];
}

/// <summary>An edge of the window that tool windows dock at.</summary>
public sealed class ToolEdgeState
{
    /// <summary><c>Right</c> or <c>Bottom</c>.</summary>
    public string Edge { get; set; } = "";

    /// <summary>The right edge's width or the bottom's height, as the user dragged it; null for the default.</summary>
    public double? Size { get; set; }

    /// <summary>Its windows fold into tabs along the window's edge, and slide out over the conversation (Visual Studio's Auto Hide).</summary>
    public bool AutoHide { get; set; }

    /// <summary>The id of the window whose tab the user chose last, which shows while the edge is docked.</summary>
    public string? Selected { get; set; }
}

/// <summary>A tool window's place.</summary>
public sealed class ToolWindowState
{
    /// <summary>Its id: its kind's, for a kind that has one window.</summary>
    public string Id { get; set; } = "";

    /// <summary>The edge it's docked at: <c>Right</c> or <c>Bottom</c>.</summary>
    public string Edge { get; set; } = "";

    /// <summary>Open, with a tab on its edge; a closed one comes back where it was.</summary>
    public bool IsOpen { get; set; } = true;
}
